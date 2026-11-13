using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// صف پایدار پیامک: ثبت پیش از ارسال، تلاش مجدد کنترل‌شده و استعلام تحویل.
/// هیچ خطای پیامکی گردش‌کار مأموریت را متوقف نمی‌کند.
/// </summary>
public sealed class SmsDispatcher(
    NovaFleetDbContext db,
    ISmsGateway gateway,
    ISettingsService settings,
    IClock clock,
    ILogger<SmsDispatcher> logger) : ISmsDispatcher
{
    public async Task QueueForMissionAsync(
        Guid missionId,
        SmsEventType eventType,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var mission = await db.Missions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == missionId, cancellationToken);
        if (mission is null)
        {
            return;
        }

        var eventKey = SmsEventNames.Of(eventType);
        var template = await db.SmsTemplates.AsNoTracking()
            .FirstOrDefaultAsync(item => item.EventKey == eventKey, cancellationToken);
        if (template is null || !template.IsActive)
        {
            return;
        }

        var recipients = SmsDefaults.ParseRecipients(
            await settings.GetValueAsync($"{SettingKeys.SmsRecipientsPrefix}{eventKey}", cancellationToken),
            eventType);

        var driver = mission.DriverId.HasValue
            ? await db.Drivers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == mission.DriverId, cancellationToken)
            : null;
        var vehicle = mission.VehicleId.HasValue
            ? await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(item => item.Id == mission.VehicleId, cancellationToken)
            : null;

        var organization = await settings.GetValueAsync(SettingKeys.OrganizationName, cancellationToken) ?? "ناوگان";
        var tokens = BuildTokens(mission, driver, vehicle, reason, organization);
        var body = Render(template.Body, tokens);

        var now = clock.UtcNow;
        var reminderMinutes = await ReadIntAsync(SettingKeys.SmsReminderMinutes, SettingKeys.DefaultSmsReminderMinutes, cancellationToken);
        var maxAttempts = await ReadIntAsync(SettingKeys.SmsMaxAttempts, SettingKeys.DefaultSmsMaxAttempts, cancellationToken);

        foreach (var recipient in recipients)
        {
            var (mobile, name) = recipient switch
            {
                "requester" => (mission.RequesterPhoneSnapshot, mission.RequesterName),
                "driver" when driver is not null => (driver.MobilePhone, driver.DisplayName),
                _ => (string.Empty, string.Empty)
            };

            if (string.IsNullOrWhiteSpace(mobile))
            {
                continue;
            }

            try
            {
                var item = new SmsQueueItem(eventType, mobile, name, body, mission.Id, "system", now, maxAttempts);
                if (eventType == SmsEventType.MissionStartReminder)
                {
                    var scheduledAt = mission.PlannedStartAtUtc.AddMinutes(-reminderMinutes);
                    item.ScheduleAt(scheduledAt <= now ? now : scheduledAt);
                }

                db.SmsQueueItems.Add(item);
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "شماره گیرنده پیامک برای مأموریت {MissionId} معتبر نیست.", missionId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RescheduleStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        await CancelStartReminderAsync(missionId, cancellationToken);
        await QueueForMissionAsync(missionId, SmsEventType.MissionStartReminder, null, cancellationToken);
    }

    public async Task CancelStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var pending = await db.SmsQueueItems
            .Where(item => item.MissionId == missionId
                && item.EventType == SmsEventType.MissionStartReminder
                && (item.Status == SmsQueueStatus.Queued
                    || item.Status == SmsQueueStatus.RetryScheduled
                    || item.Status == SmsQueueStatus.Sending))
            .ToListAsync(cancellationToken);

        foreach (var item in pending)
        {
            item.Cancel("یادآوری پیش از ارسال به‌دلیل تغییر زمان مأموریت لغو شد.", now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessQueueAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        if (!await IsSendingEnabledAsync(cancellationToken))
        {
            logger.LogDebug("ارسال پیامک در سامانه غیرفعال است؛ صف دست‌نخورده می‌ماند.");
            return 0;
        }

        var now = clock.UtcNow;
        var items = await db.SmsQueueItems
            .Where(item => (item.Status == SmsQueueStatus.Queued || item.Status == SmsQueueStatus.RetryScheduled)
                && (item.NextAttemptAtUtc == null || item.NextAttemptAtUtc <= now))
            .OrderBy(item => item.NextAttemptAtUtc)
            .Take(Math.Clamp(batchSize, 1, 100))
            .ToListAsync(cancellationToken);

        var retryMinutes = await ReadIntAsync(SettingKeys.SmsRetryMinutes, SettingKeys.DefaultSmsRetryMinutes, cancellationToken);

        foreach (var item in items)
        {
            try
            {
                item.MarkSending(now);
                await db.SaveChangesAsync(cancellationToken);

                var result = await gateway.SendAsync(
                    new SmsOutboundMessage(item.RecipientMobile, item.MessageBody, item.Id.ToString()),
                    cancellationToken);

                if (result.Accepted)
                {
                    item.MarkAccepted(result.ProviderReference, clock.UtcNow);
                }
                else if (result.IsPermanentFailure)
                {
                    item.MarkPermanentFailure(result.Error ?? "خطای دائمی سرویس‌دهنده.", clock.UtcNow);
                }
                else
                {
                    var delay = TimeSpan.FromMinutes(retryMinutes * Math.Max(item.AttemptCount, 1));
                    item.ScheduleRetry(result.Error ?? "خطای موقت سرویس‌دهنده.", clock.UtcNow.Add(delay), clock.UtcNow);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "پردازش پیامک {SmsId} ناموفق بود.", item.Id);
                var delay = TimeSpan.FromMinutes(retryMinutes * Math.Max(item.AttemptCount, 1));
                try
                {
                    if (item.Status == SmsQueueStatus.Sending)
                    {
                        item.ScheduleRetry("خطای غیرمنتظره در ارسال.", clock.UtcNow.Add(delay), clock.UtcNow);
                    }
                }
                catch (InvalidOperationException)
                {
                    // وضعیت پیام میان دو تلاش تغییر کرده است؛ در تلاش بعدی دوباره بررسی می‌شود.
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return items.Count;
    }

    public async Task<int> PollDeliveryAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        if (!await IsSendingEnabledAsync(cancellationToken))
        {
            return 0;
        }

        var now = clock.UtcNow;
        var pollingMinutes = await ReadIntAsync(SettingKeys.SmsPollingMinutes, SettingKeys.DefaultSmsPollingMinutes, cancellationToken);
        var maxPolls = await ReadIntAsync(SettingKeys.SmsMaxPollingAttempts, SettingKeys.DefaultSmsMaxPollingAttempts, cancellationToken);
        var cutoff = now.AddMinutes(-pollingMinutes);

        var items = await db.SmsQueueItems
            .Where(item => item.Status == SmsQueueStatus.Accepted
                && item.ProviderReference != null
                && item.DeliveryPollCount < maxPolls
                && (item.AcceptedAtUtc == null || item.AcceptedAtUtc <= cutoff))
            .OrderBy(item => item.AcceptedAtUtc)
            .Take(Math.Clamp(batchSize, 1, 100))
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.RecordDeliveryPoll(now);
            try
            {
                var result = await gateway.GetDeliveryStatusAsync(item.ProviderReference!, cancellationToken);
                switch (result.State)
                {
                    case SmsDeliveryState.Delivered:
                        item.MarkDelivered(clock.UtcNow);
                        break;
                    case SmsDeliveryState.Failed:
                        item.MarkFailed(result.RawStatus ?? "تحویل ناموفق.", clock.UtcNow);
                        break;
                    default:
                        item.RecordUnknownDeliveryStatus(result.RawStatus ?? "unknown", clock.UtcNow);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "استعلام تحویل پیامک {SmsId} ناموفق بود.", item.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return items.Count;
    }

    private async Task<bool> IsSendingEnabledAsync(CancellationToken cancellationToken) =>
        bool.TryParse(await settings.GetValueAsync(SettingKeys.SmsEnabled, cancellationToken), out var enabled)
        && enabled;

    private async Task<int> ReadIntAsync(string key, int fallback, CancellationToken cancellationToken)
    {
        var value = await settings.GetValueAsync(key, cancellationToken);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static Dictionary<string, string> BuildTokens(
        Mission mission,
        Driver? driver,
        Vehicle? vehicle,
        string? reason,
        string organization)
    {
        var route = string.Join(" → ", mission.GetIntermediateStops());
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["missionNumber"] = mission.MissionNumber,
            ["title"] = mission.Title,
            ["requester"] = mission.RequesterName,
            ["requesterMobile"] = mission.RequesterPhoneSnapshot,
            ["driver"] = driver?.DisplayName ?? "—",
            ["driverMobile"] = driver?.MobilePhone ?? "—",
            ["vehicle"] = vehicle?.DisplayName ?? "—",
            ["plate"] = vehicle?.PlateDisplay ?? "—",
            ["origin"] = mission.Origin,
            ["destination"] = mission.Destination,
            ["route"] = string.IsNullOrWhiteSpace(route) ? "—" : route,
            ["start"] = JalaliDate.FormatDateTime(mission.PlannedStartAtUtc),
            ["end"] = JalaliDate.FormatDateTime(mission.PlannedEndAtUtc),
            ["type"] = mission.Type == MissionType.Freight ? "بار" : "مسافر",
            ["reason"] = string.IsNullOrWhiteSpace(reason) ? "—" : reason,
            ["distance"] = mission.DistanceKm?.ToString() ?? "—",
            ["orderNumber"] = mission.MissionOrderNumber ?? "—",
            ["orderDate"] = mission.MissionOrderDate?.ToString("yyyy/MM/dd") ?? "—",
            ["organization"] = organization
        };
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var result = template;
        foreach (var (key, value) in tokens)
        {
            result = result.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}

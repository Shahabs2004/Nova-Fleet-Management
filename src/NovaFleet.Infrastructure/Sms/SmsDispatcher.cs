using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

        var storedRecipients = await settings.GetValueAsync(
            $"{SettingKeys.SmsRecipientsPrefix}{eventKey}", cancellationToken);
        IReadOnlyList<string> recipients;
        try
        {
            recipients = SmsDefaults.ParseRecipients(storedRecipients, eventType);
        }
        catch (ValidationAppException ex)
        {
            logger.LogWarning(ex,
                "تنظیمات گیرنده پیامک برای رخداد {EventKey} مأموریت {MissionId} معتبر نیست؛ پیامکی در صف ثبت نشد.",
                eventKey, missionId);
            return;
        }

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
                    || item.Status == SmsQueueStatus.RetryScheduled))
            .ToListAsync(cancellationToken);

        foreach (var item in pending)
        {
            item.Cancel("یادآوری پیش از ارسال به‌دلیل تغییر زمان مأموریت لغو شد.", now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessQueueAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        // entity شکست‌خوردهٔ ذخیره قبلی نباید در دست بعدی، بازیابی را بازنویسی کند.
        db.ChangeTracker.Clear();
        // بازیابی حتی وقتی ارسال غیرفعال است انجام می‌شود تا ارسال‌های رهاشده برای همیشه Sending نمانند.
        await RecoverStaleSendsAsync(100, cancellationToken);
        if (!await IsSendingEnabledAsync(cancellationToken))
        {
            logger.LogDebug("ارسال پیامک در سامانه غیرفعال است؛ صف دست‌نخورده می‌ماند.");
            return 0;
        }

        var boundedBatch = Math.Clamp(batchSize, 1, 100);
        var retryMinutes = Math.Clamp(
            await ReadIntAsync(SettingKeys.SmsRetryMinutes, SettingKeys.DefaultSmsRetryMinutes, cancellationToken),
            1, 24 * 60);
        var configuredMode = await settings.GetValueAsync(SettingKeys.SmsMode, cancellationToken);
        var mode = SmsGatewayModes.Parse(configuredMode);
        var processed = 0;

        for (var index = 0; index < boundedBatch; index++)
        {
            // فقط همین آیتم درست پیش از I/O برداشت می‌شود؛ آیتم‌های بعدی در صف امن می‌مانند.
            var items = await ClaimAsync(1, clock.UtcNow, mode, cancellationToken);
            if (items.Count == 0)
            {
                break;
            }

            var item = items[0];
            processed++;
            try
            {
                var selectedGateway = SelectGateway(item.GatewayMode);
                SmsSendResult result;
                try
                {
                    result = await selectedGateway.SendAsync(
                        new SmsOutboundMessage(item.RecipientMobile, item.MessageBody, item.Id.ToString()),
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // پس از ورود به درگاه، هیچ استثنای غیرمنتظره‌ای به تلاش خودکار دوباره تبدیل نمی‌شود.
                    logger.LogWarning(ex, "نتیجه ارسال پیامک {SmsId} قابل تعیین نیست.", item.Id);
                    result = SmsSendResult.Indeterminate("نتیجه ارسال به سرویس پیامک قابل تعیین نیست.");
                }

                var sendStartedAtUtc = item.SendStartedAtUtc!.Value;
                if (!string.IsNullOrWhiteSpace(result.ProviderReference))
                {
                    await StoreProviderReferenceAsync(
                        item.Id, sendStartedAtUtc, result.ProviderReference, clock.UtcNow, cancellationToken);
                }

                await CompleteClaimAsync(item.Id, sendStartedAtUtc, result, retryMinutes, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // خرابی ذخیره نهایی ارسال را Sending نگه می‌دارد؛ بازیابی ده‌دقیقه‌ای آن را به Indeterminate می‌برد.
                logger.LogWarning(ex, "ذخیره نتیجه پیامک {SmsId} ناموفق بود؛ بازیابی زمان‌دار انجام می‌شود.", item.Id);
            }
        }

        return processed;
    }

    public async Task<int> PollDeliveryAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        if (!await IsSendingEnabledAsync(cancellationToken))
        {
            return 0;
        }

        var now = clock.UtcNow;
        var maxPolls = Math.Max(0,
            await ReadIntAsync(SettingKeys.SmsMaxPollingAttempts, SettingKeys.DefaultSmsMaxPollingAttempts, cancellationToken));
        var candidates = await db.SmsQueueItems.AsNoTracking()
            .Where(item => item.Status == SmsQueueStatus.Accepted
                && item.ProviderReference != null
                && item.DeliveryPollCount < maxPolls
                && (item.NextDeliveryPollAtUtc == null || item.NextDeliveryPollAtUtc <= now))
            .OrderBy(item => item.NextDeliveryPollAtUtc)
            .ThenBy(item => item.AcceptedAtUtc)
            .Select(item => item.Id)
            .Take(Math.Clamp(batchSize, 1, 100))
            .ToListAsync(cancellationToken);

        var polled = 0;
        foreach (var id in candidates)
        {
            db.ChangeTracker.Clear();
            var item = await db.SmsQueueItems.FirstOrDefaultAsync(row =>
                row.Id == id
                && row.Status == SmsQueueStatus.Accepted
                && row.DeliveryPollCount < maxPolls
                && (row.NextDeliveryPollAtUtc == null || row.NextDeliveryPollAtUtc <= now), cancellationToken);
            if (item is null)
            {
                continue;
            }

            // رزرو سررسید قبل از I/O؛ رقابت workerها فقط یکی را به سرویس‌دهنده می‌رساند.
            item.RecordDeliveryPoll(now, NextPollAt(item.DeliveryPollCount + 1, now));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "رزرو استعلام تحویل پیامک {SmsId} ناموفق بود.", id);
                continue;
            }

            var reservedVersion = item.RowVersion.ToArray();
            polled++;
            try
            {
                var selectedGateway = SelectGateway(item.GatewayMode);
                var result = await selectedGateway.GetDeliveryStatusAsync(item.ProviderReference!, cancellationToken);
                db.ChangeTracker.Clear();
                var current = await db.SmsQueueItems.FirstOrDefaultAsync(row =>
                    row.Id == id
                    && row.Status == SmsQueueStatus.Accepted
                    && row.RowVersion == reservedVersion, cancellationToken);
                if (current is null)
                {
                    continue;
                }

                switch (result.State)
                {
                    case SmsDeliveryState.Delivered:
                        current.MarkDelivered(clock.UtcNow);
                        break;
                    case SmsDeliveryState.Failed:
                        current.MarkFailed(result.RawStatus ?? "تحویل ناموفق.", clock.UtcNow);
                        break;
                    default:
                        current.RecordUnknownDeliveryStatus(result.RawStatus ?? "unknown", clock.UtcNow);
                        break;
                }

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "استعلام تحویل پیامک {SmsId} ناموفق بود.", id);
            }
        }

        return polled;
    }

    public async Task<int> RecoverStaleSendsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var cutoff = now.AddMinutes(-10);
        var ids = await db.SmsQueueItems.AsNoTracking()
            .Where(item => item.Status == SmsQueueStatus.Sending
                && (item.SendStartedAtUtc != null && item.SendStartedAtUtc <= cutoff
                    || item.SendStartedAtUtc == null && item.LastAttemptAtUtc != null && item.LastAttemptAtUtc <= cutoff))
            .OrderBy(item => item.SendStartedAtUtc)
            .Select(item => item.Id)
            .Take(Math.Clamp(batchSize, 1, 100))
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        return await db.SmsQueueItems
            .Where(item => ids.Contains(item.Id)
                && item.Status == SmsQueueStatus.Sending
                && (item.SendStartedAtUtc != null && item.SendStartedAtUtc <= cutoff
                    || item.SendStartedAtUtc == null && item.LastAttemptAtUtc != null && item.LastAttemptAtUtc <= cutoff))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, SmsQueueStatus.Indeterminate)
                .SetProperty(item => item.LastError, "ارسال پس از ده دقیقه بدون نتیجه نهایی رها شده است؛ نیازمند بررسی اپراتور.")
                .SetProperty(item => item.NextAttemptAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.NextDeliveryPollAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.UpdatedAtUtc, now), cancellationToken);
    }

    private async Task<IReadOnlyList<SmsQueueItem>> ClaimAsync(
        int batchSize, DateTimeOffset now, SmsGatewayMode mode, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlServer())
        {
            return await ClaimSqlServerAsync(batchSize, now, mode, cancellationToken);
        }

        // SQLite فقط برای آزمون‌های واحد است؛ ادعای ایمنی رقابت SQL Server با این مسیر انجام نمی‌شود.
        var items = await db.SmsQueueItems
            .Where(item => (item.Status == SmsQueueStatus.Queued || item.Status == SmsQueueStatus.RetryScheduled)
                && (item.NextAttemptAtUtc == null || item.NextAttemptAtUtc <= now))
            .OrderBy(item => item.NextAttemptAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return items;
        }

        foreach (var item in items)
        {
            item.MarkSending(now, (int)mode);
        }

        await db.SaveChangesAsync(cancellationToken);
        return items;
    }

    private async Task<IReadOnlyList<SmsQueueItem>> ClaimSqlServerAsync(
        int batchSize, DateTimeOffset now, SmsGatewayMode mode, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                WITH Claimed AS (
                    SELECT TOP (@batchSize) *
                    FROM [SmsQueueItems] WITH (UPDLOCK, READPAST)
                    WHERE [Status] IN (1, 5)
                      AND ([NextAttemptAtUtc] IS NULL OR [NextAttemptAtUtc] <= @now)
                    ORDER BY [NextAttemptAtUtc], [CreatedAtUtc]
                )
                UPDATE Claimed
                SET [Status] = 2,
                    [GatewayMode] = @mode,
                    [SendStartedAtUtc] = @now,
                    [NextAttemptAtUtc] = NULL,
                    [NextDeliveryPollAtUtc] = NULL,
                    [DeliveryPollCount] = 0,
                    [AcceptedAtUtc] = NULL,
                    [AttemptCount] = [AttemptCount] + 1,
                    [LastAttemptAtUtc] = @now,
                    [UpdatedAtUtc] = @now
                OUTPUT inserted.[Id];
                """;
            command.Parameters.Add(new SqlParameter("@batchSize", batchSize));
            command.Parameters.Add(new SqlParameter("@now", now));
            command.Parameters.Add(new SqlParameter("@mode", (int)mode));
            var ids = new List<Guid>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    ids.Add(reader.GetGuid(0));
                }
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            if (ids.Count == 0)
            {
                return [];
            }

            return await db.SmsQueueItems.AsNoTracking()
                .Where(item => ids.Contains(item.Id))
                .ToListAsync(cancellationToken);
        }
        catch
        {
            if (!committed && transaction.Connection is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    private async Task StoreProviderReferenceAsync(
        Guid id, DateTimeOffset sendStartedAtUtc, string providerReference, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // یک context مستقل حتى اگر ذخیره نهایی با interceptor شکست بخورد، شاهد پذیرش درگاه از بین نرود.
        var options = (DbContextOptions<NovaFleetDbContext>)db.GetService<IDbContextOptions>();
        await using var referenceDb = new NovaFleetDbContext(options);
        var reference = providerReference.Trim();
        await referenceDb.SmsQueueItems
            .Where(row => row.Id == id
                && row.SendStartedAtUtc == sendStartedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.ProviderReference, reference)
                .SetProperty(row => row.UpdatedAtUtc, now), cancellationToken);
    }

    private async Task CompleteClaimAsync(
        Guid id, DateTimeOffset sendStartedAtUtc, SmsSendResult result, int retryMinutes,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var item = await db.SmsQueueItems.FirstOrDefaultAsync(row =>
            row.Id == id
            && row.Status == SmsQueueStatus.Sending
            && row.SendStartedAtUtc == sendStartedAtUtc, cancellationToken);
        if (item is null)
        {
            // اپراتور یا بازیابی هم‌زمان تصمیم گرفته است؛ نتیجه دیروز هیچ تصمیمی را بازنویسی نمی‌کند.
            return;
        }

        var now = clock.UtcNow;
        if (result.IsIndeterminate)
        {
            item.MarkIndeterminate(result.Error ?? "نتیجه ارسال نامشخص است.", result.RawStatus, now, result.ProviderReference);
        }
        else if (result.Accepted && !string.IsNullOrWhiteSpace(result.ProviderReference))
        {
            item.MarkAccepted(result.ProviderReference, now);
        }
        else if (result.Accepted)
        {
            item.MarkIndeterminate("سرویس پیامک پیام را پذیرفت اما شناسه قابل استفاده برنگرداند.", result.RawStatus, now);
        }
        else if (result.IsPermanentFailure)
        {
            item.MarkPermanentFailure(result.Error ?? "خطای دائمی سرویس‌دهنده.", now);
        }
        else
        {
            var delay = TimeSpan.FromMinutes((long)retryMinutes * Math.Max(item.AttemptCount, 1));
            item.ScheduleRetry(result.Error ?? "خطای موقت سرویس‌دهنده.", now.Add(delay), now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ذخیره نتیجه نهایی پیامک {SmsId} ناموفق بود.", id);
        }
    }

    private ISmsGateway SelectGateway(int storedMode) =>
        gateway is ISmsGatewaySelector selector
            ? selector.ForMode((SmsGatewayMode)storedMode)
            : gateway;

    private static DateTimeOffset NextPollAt(int completedPolls, DateTimeOffset now)
    {
        var maximum = TimeSpan.FromHours(24);
        var delay = completedPolls switch
        {
            <= 1 => TimeSpan.FromSeconds(10),
            2 => TimeSpan.FromSeconds(30),
            3 => TimeSpan.FromSeconds(60),
            _ => TimeSpan.FromMinutes(5),
        };
        for (var poll = 4; poll < completedPolls && delay < maximum; poll++)
        {
            delay = delay * 2 > maximum ? maximum : delay * 2;
        }

        return now.Add(delay);
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

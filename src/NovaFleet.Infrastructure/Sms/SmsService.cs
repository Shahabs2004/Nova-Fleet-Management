using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>مشاهده صف/سوابق پیامک، ارسال مجدد دستی و مدیریت قالب‌ها و گیرندگان.</summary>
public sealed class SmsService(
    NovaFleetDbContext db,
    ISettingsService settings,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IClock clock) : ISmsService
{
    public async Task<PagedResult<SmsQueueItemDto>> ListAsync(SmsQuery query, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Notifications.SmsView);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var source =
            from item in db.SmsQueueItems.AsNoTracking()
            join mission in db.Missions.AsNoTracking() on item.MissionId equals mission.Id into missions
            from mission in missions.DefaultIfEmpty()
            select new { item, MissionNumber = mission == null ? "—" : mission.MissionNumber };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(row =>
                row.item.RecipientMobile.Contains(search)
                || row.item.RecipientName.Contains(search)
                || row.MissionNumber.Contains(search));
        }

        if (query.Status.HasValue)
        {
            source = source.Where(row => row.item.Status == query.Status.Value);
        }

        if (query.FromUtc.HasValue)
        {
            source = source.Where(row => row.item.CreatedAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            source = source.Where(row => row.item.CreatedAtUtc <= query.ToUtc.Value);
        }

        var total = await source.CountAsync(cancellationToken);
        var items = await source
            .OrderByDescending(row => row.item.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new SmsQueueItemDto(
                row.item.Id,
                row.item.EventKey,
                row.item.EventType,
                row.item.MissionId,
                row.MissionNumber,
                row.item.RecipientName,
                row.item.RecipientMobile,
                row.item.MessageBody,
                row.item.Status,
                row.item.AttemptCount,
                row.item.MaxAttempts,
                row.item.ProviderReference,
                row.item.LastError,
                row.item.CreatedAtUtc,
                row.item.LastAttemptAtUtc,
                row.item.AcceptedAtUtc,
                row.item.DeliveredAtUtc,
                row.item.NextAttemptAtUtc,
                row.item.Status == SmsQueueStatus.Failed || row.item.Status == SmsQueueStatus.PermanentFailure))
            .ToListAsync(cancellationToken);

        return new PagedResult<SmsQueueItemDto>(items, total, page, pageSize);
    }

    public async Task RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Notifications.SmsRetry);
        var item = await db.SmsQueueItems.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundAppException("پیامک پیدا نشد.");

        item.QueueManualRetry(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("RetrySms", "SmsQueueItem", id.ToString(), "Success",
            After: new { item.EventKey, item.RecipientMobile }), cancellationToken);
    }

    public async Task<IReadOnlyList<SmsTemplateDto>> ListTemplatesAsync(CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(
            PermissionCatalog.Notifications.SmsView,
            PermissionCatalog.Notifications.SmsTemplatesManage);
        var templates = await db.SmsTemplates.AsNoTracking().ToListAsync(cancellationToken);
        var result = new List<SmsTemplateDto>();

        foreach (var type in MissionEvents)
        {
            var eventKey = SmsEventNames.Of(type);
            var template = templates.FirstOrDefault(item => item.EventKey == eventKey);
            var recipients = SmsDefaults.ParseRecipients(
                await settings.GetValueAsync($"{SettingKeys.SmsRecipientsPrefix}{eventKey}", cancellationToken),
                type);

            result.Add(new SmsTemplateDto(
                type,
                eventKey,
                SmsDefaults.TitleFor(type),
                template?.Body ?? SmsDefaults.BodyFor(type),
                template?.IsActive ?? true,
                recipients));
        }

        return result;
    }

    public async Task<SmsTemplateDto> UpdateTemplateAsync(
        SmsEventType eventType,
        UpdateSmsTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Notifications.SmsTemplatesManage);
        if (!MissionEvents.Contains(eventType))
        {
            throw new ValidationAppException("رخداد پیامک انتخاب‌شده معتبر نیست.");
        }

        var eventKey = SmsEventNames.Of(eventType);
        var template = await db.SmsTemplates.FirstOrDefaultAsync(item => item.EventKey == eventKey, cancellationToken);
        if (template is null)
        {
            template = new SmsTemplate(eventType, request.Body, request.IsActive);
            db.SmsTemplates.Add(template);
        }
        else
        {
            template.Update(request.Body, request.IsActive);
        }

        var recipients = request.Recipients
            .Where(item => item is "requester" or "driver")
            .Distinct()
            .ToList();

        SetSetting($"{SettingKeys.SmsRecipientsPrefix}{eventKey}", string.Join(',', recipients));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("UpdateSmsTemplate", "SmsTemplate", eventKey, "Success",
            After: new { EventType = eventType, request.IsActive, Recipients = recipients }), cancellationToken);

        return new SmsTemplateDto(
            eventType,
            eventKey,
            SmsDefaults.TitleFor(eventType),
            template.Body,
            template.IsActive,
            recipients.Count > 0 ? recipients : SmsDefaults.RecipientsFor(eventType));
    }

    public async Task<Guid> QueueTestAsync(SmsTestRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.ConnectionsManage);
        if (!bool.TryParse(await settings.GetValueAsync(SettingKeys.SmsEnabled, cancellationToken), out var enabled)
            || !enabled)
        {
            throw new ValidationAppException("ارسال پیامک در سامانه غیرفعال است؛ پیش از تست آن را فعال کنید.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new ValidationAppException("متن پیامک آزمایشی خالی است.");
        }

        var item = new SmsQueueItem(
            SmsEventType.ManualTest,
            request.Mobile,
            "پیام آزمایشی",
            request.Body,
            null,
            currentUser.Username,
            clock.UtcNow,
            await ReadMaxAttemptsAsync(cancellationToken));

        db.SmsQueueItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("QueueTestSms", "SmsQueueItem", item.Id.ToString(), "Success",
            After: new { item.RecipientMobile }), cancellationToken);
        return item.Id;
    }

    public async Task<SmsStatusSummaryDto> GetStatusSummaryAsync(CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Notifications.SmsView);
        var today = clock.UtcNow.Date;
        var counts = await db.SmsQueueItems.AsNoTracking()
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int Count(SmsQueueStatus status) => counts.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
        var todayCount = await db.SmsQueueItems.CountAsync(item => item.CreatedAtUtc >= today, cancellationToken);

        return new SmsStatusSummaryDto(
            Count(SmsQueueStatus.Queued) + Count(SmsQueueStatus.RetryScheduled) + Count(SmsQueueStatus.Sending),
            Count(SmsQueueStatus.Accepted),
            Count(SmsQueueStatus.Delivered),
            Count(SmsQueueStatus.Failed),
            Count(SmsQueueStatus.PermanentFailure),
            todayCount);
    }

    private static readonly SmsEventType[] MissionEvents =
    [
        SmsEventType.MissionApproved,
        SmsEventType.MissionRejected,
        SmsEventType.MissionChanged,
        SmsEventType.DriverOrVehicleChanged,
        SmsEventType.MissionCancelled,
        SmsEventType.WithdrawalApproved,
        SmsEventType.WithdrawalRejected,
        SmsEventType.MissionStartReminder,
        SmsEventType.MissionCompleted
    ];

    private async Task<int> ReadMaxAttemptsAsync(CancellationToken cancellationToken)
    {
        var value = await settings.GetValueAsync(SettingKeys.SmsMaxAttempts, cancellationToken);
        return int.TryParse(value, out var attempts) && attempts > 0
            ? attempts
            : SettingKeys.DefaultSmsMaxAttempts;
    }

    private void SetSetting(string key, string value)
    {
        var existing = db.SystemSettings.FirstOrDefault(item => item.Key == key);
        if (existing is null)
        {
            db.SystemSettings.Add(new Domain.Configuration.SystemSetting(key, value));
        }
        else
        {
            existing.SetValue(value, clock.UtcNow);
        }
    }
}

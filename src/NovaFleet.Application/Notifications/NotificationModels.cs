using NovaFleet.Application.Common;
using NovaFleet.Domain.Notifications;

namespace NovaFleet.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Message,
    string EventKey,
    string? ReferenceType,
    string? ReferenceId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int UnreadCount, int TotalCount);

public interface INotificationService
{
    Task<NotificationPageDto> ListAsync(bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationDto>> ListRecentAsync(int count, CancellationToken cancellationToken = default);

    Task<int> UnreadCountAsync(CancellationToken cancellationToken = default);

    Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(CancellationToken cancellationToken = default);
}

public sealed record SmsQueueItemDto(
    Guid Id,
    string EventKey,
    SmsEventType EventType,
    Guid? MissionId,
    string MissionNumber,
    string RecipientName,
    string RecipientMobile,
    string MessageBody,
    SmsQueueStatus Status,
    int AttemptCount,
    int MaxAttempts,
    string? ProviderReference,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? AcceptedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    bool CanRetry);

public sealed record SmsQuery(
    string? Search,
    SmsQueueStatus? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page,
    int PageSize);

public sealed record SmsTemplateDto(
    SmsEventType EventType,
    string EventKey,
    string Title,
    string Body,
    bool IsActive,
    IReadOnlyList<string> Recipients);

public sealed record UpdateSmsTemplateRequest(string Body, bool IsActive, IReadOnlyList<string> Recipients);

public sealed record SmsTestRequest(string Mobile, string Body);

public interface ISmsService
{
    Task<PagedResult<SmsQueueItemDto>> ListAsync(SmsQuery query, CancellationToken cancellationToken = default);

    Task RetryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsTemplateDto>> ListTemplatesAsync(CancellationToken cancellationToken = default);

    Task<SmsTemplateDto> UpdateTemplateAsync(SmsEventType eventType, UpdateSmsTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>ثبت پیامک آزمایشی در همان صف؛ مسیر ارسال با پیامک واقعی یکی است.</summary>
    Task<Guid> QueueTestAsync(SmsTestRequest request, CancellationToken cancellationToken = default);

    Task<SmsStatusSummaryDto> GetStatusSummaryAsync(CancellationToken cancellationToken = default);
}

public sealed record SmsStatusSummaryDto(int Queued, int Accepted, int Delivered, int Failed, int PermanentFailure, int TodayCount);

/// <summary>پردازش صف پیامک؛ در سرویس پس‌زمینه فراخوانی می‌شود و هرگز گردش‌کار مأموریت را متوقف نمی‌کند.</summary>
public interface ISmsDispatcher
{
    Task<int> ProcessQueueAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>استعلام وضعیت پیام‌های پذیرفته‌شده تا سقف تلاش تنظیم‌شده.</summary>
    Task<int> PollDeliveryAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>ثبت پیامک‌های یک رخداد مأموریت برای گیرندگان تنظیم‌شده.</summary>
    Task QueueForMissionAsync(Guid missionId, SmsEventType eventType, string? reason, CancellationToken cancellationToken = default);

    /// <summary>لغو یادآوری ارسال‌نشده و زمان‌بندی مجدد پس از تغییر زمان شروع.</summary>
    Task RescheduleStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task CancelStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default);
}

using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Notifications;

/// <summary>
/// یک پیامک در صف پایدار. ثبت در پایگاه داده پیش از تماس با سرویس‌دهنده انجام می‌شود
/// تا خطای شبکه یا قطع سرویس، پیام را از بین نبرد و گردش‌کار مأموریت را متوقف نکند.
/// </summary>
public sealed class SmsQueueItem : Entity
{
    public const int DefaultMaxAttempts = 5;

    private SmsQueueItem()
    {
    }

    public SmsEventType EventType { get; private set; }

    /// <summary>کلید پایدار رخداد مانند <c>mission.approved</c> برای جست‌وجو و گزارش.</summary>
    public string EventKey { get; private set; } = string.Empty;

    public Guid? MissionId { get; private set; }

    public string RecipientMobile { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    public string MessageBody { get; private set; } = string.Empty;

    public SmsQueueStatus Status { get; private set; } = SmsQueueStatus.Queued;

    public int AttemptCount { get; private set; }

    public int MaxAttempts { get; private set; } = DefaultMaxAttempts;

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    /// <summary>شناسه پیام در سرویس‌دهنده؛ برای پیگیری تحویل.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>آخرین پیام خطا یا وضعیت خام سرویس‌دهنده؛ بدون داده حساس.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptAtUtc { get; private set; }

    public int DeliveryPollCount { get; private set; }

    public string? CreatedBy { get; private set; }

    public SmsQueueItem(
        SmsEventType eventType,
        string recipientMobile,
        string recipientName,
        string messageBody,
        Guid? missionId = null,
        string? createdBy = null,
        DateTimeOffset? nowUtc = null)
    {
        if (!Enum.IsDefined(eventType))
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), "نوع رخداد پیامک معتبر نیست.");
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        EventType = eventType;
        EventKey = SmsEventNames.Of(eventType);
        MissionId = missionId;
        RecipientMobile = NormalizeMobile(recipientMobile);
        RecipientName = (recipientName ?? string.Empty).Trim();
        MessageBody = Required(messageBody, 1000, nameof(messageBody));
        CreatedBy = createdBy?.Trim();
        UpdatedAtUtc = now;
        NextAttemptAtUtc = now;
    }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool CanRetryManually => Status is SmsQueueStatus.Failed or SmsQueueStatus.PermanentFailure;

    /// <summary>آماده‌برداشت برای worker؛ تنها پیام‌های صف‌شده و سررسیدشده.</summary>
    public bool IsDue(DateTimeOffset nowUtc) =>
        Status is SmsQueueStatus.Queued or SmsQueueStatus.RetryScheduled
        && (NextAttemptAtUtc is null || NextAttemptAtUtc <= nowUtc);

    /// <summary>زمان‌بندی ارسال در آینده؛ برای یادآوری پیش از شروع مأموریت.</summary>
    public void ScheduleAt(DateTimeOffset atUtc)
    {
        EnsureStatus(SmsQueueStatus.Queued);
        NextAttemptAtUtc = atUtc;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RecordDeliveryPoll(DateTimeOffset nowUtc)
    {
        DeliveryPollCount++;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkSending(DateTimeOffset nowUtc)
    {
        EnsureStatus(SmsQueueStatus.Queued, SmsQueueStatus.RetryScheduled);
        Status = SmsQueueStatus.Sending;
        AttemptCount++;
        LastAttemptAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>سرویس‌دهنده پیام را پذیرفت؛ تحویل هنوز قطعی نیست.</summary>
    public void MarkAccepted(string? providerReference, DateTimeOffset nowUtc)
    {
        EnsureStatus(SmsQueueStatus.Sending, SmsQueueStatus.Accepted);
        Status = SmsQueueStatus.Accepted;
        ProviderReference = providerReference?.Trim();
        AcceptedAtUtc = nowUtc;
        LastError = null;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkDelivered(DateTimeOffset nowUtc)
    {
        EnsureStatus(SmsQueueStatus.Accepted, SmsQueueStatus.Delivered);
        Status = SmsQueueStatus.Delivered;
        DeliveredAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>خطای موقت؛ اگر سهمیه تلاش تمام شده باشد پیام Failed می‌شود تا نیازمند اقدام دستی باشد.</summary>
    public void ScheduleRetry(string error, DateTimeOffset nextAttemptAtUtc, DateTimeOffset nowUtc)
    {
        EnsureStatus(SmsQueueStatus.Sending, SmsQueueStatus.RetryScheduled, SmsQueueStatus.Accepted);
        LastError = Truncate(error, 500);
        UpdatedAtUtc = nowUtc;
        if (AttemptCount >= MaxAttempts)
        {
            Status = SmsQueueStatus.Failed;
            NextAttemptAtUtc = null;
            return;
        }

        Status = SmsQueueStatus.RetryScheduled;
        NextAttemptAtUtc = nextAttemptAtUtc;
    }

    public void MarkFailed(string error, DateTimeOffset nowUtc)
    {
        EnsureStatus(SmsQueueStatus.Sending, SmsQueueStatus.RetryScheduled, SmsQueueStatus.Queued, SmsQueueStatus.Accepted);
        Status = SmsQueueStatus.Failed;
        LastError = Truncate(error, 500);
        NextAttemptAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>خطای دائمی مانند شماره نامعتبر یا رد قطعی سرویس‌دهنده؛ تلاش خودکار متوقف می‌شود.</summary>
    public void MarkPermanentFailure(string error, DateTimeOffset nowUtc)
    {
        EnsureStatus(SmsQueueStatus.Sending, SmsQueueStatus.RetryScheduled, SmsQueueStatus.Queued, SmsQueueStatus.Accepted);
        Status = SmsQueueStatus.PermanentFailure;
        LastError = Truncate(error, 500);
        NextAttemptAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void Cancel(string reason, DateTimeOffset nowUtc)
    {
        if (Status.IsTerminal())
        {
            throw new InvalidOperationException("پیامک پایان‌یافته قابل لغو نیست.");
        }

        Status = SmsQueueStatus.Cancelled;
        LastError = Truncate(reason, 500);
        NextAttemptAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>تلاش مجدد دستی توسط اپراتور؛ سهمیه تلاش بازنشانی می‌شود.</summary>
    public void QueueManualRetry(DateTimeOffset nowUtc)
    {
        if (!CanRetryManually)
        {
            throw new InvalidOperationException("فقط پیامک ناموفق قابل ارسال مجدد است.");
        }

        Status = SmsQueueStatus.Queued;
        AttemptCount = 0;
        NextAttemptAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>وضعیت تحویل ناشناخته؛ پیام همچنان در انتظار پیگیری می‌ماند.</summary>
    public void RecordUnknownDeliveryStatus(string rawStatus, DateTimeOffset nowUtc)
    {
        EnsureStatus(SmsQueueStatus.Accepted);
        LastError = Truncate($"وضعیت ناشناخته تحویل: {rawStatus}", 500);
        UpdatedAtUtc = nowUtc;
    }

    private void EnsureStatus(params SmsQueueStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"تغییر وضعیت پیامک از «{Status}» در این مرحله مجاز نیست.");
        }
    }

    private static string NormalizeMobile(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var normalized = new string(value.Where(char.IsDigit).ToArray());
        if (normalized.Length < 10 || normalized.Length > 13)
        {
            throw new ArgumentException("شماره گیرنده پیامک معتبر نیست.", nameof(value));
        }

        return normalized;
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

namespace NovaFleet.Domain.Notifications;

/// <summary>
/// چرخه عمر یک پیامک در صف. «پذیرش» و «تحویل» مفاهیم جدا هستند؛
/// تنها وضعیت‌های دارای نگاشت معتبر به «تحویل‌شده» تبدیل می‌شوند و وضعیت ناشناخته Failed نمی‌شود بلکه نامشخص می‌ماند.
/// </summary>
public enum SmsQueueStatus
{
    /// <summary>در انتظار ارسال به سرویس‌دهنده.</summary>
    Queued = 1,

    /// <summary>ارسال در جریان است؛ برای جلوگیری از ارسال تکراری در این وضعیت دوباره برداشته نمی‌شود.</summary>
    Sending = 2,

    /// <summary>سرویس‌دهنده پیام را پذیرفته اما تحویل نهایی هنوز تأیید نشده است.</summary>
    Accepted = 3,

    /// <summary>سرویس‌دهنده تحویل به گیرنده را تأیید کرده است.</summary>
    Delivered = 4,

    /// <summary>خطای موقت؛ تلاش بعدی در زمان تعیین‌شده انجام می‌شود.</summary>
    RetryScheduled = 5,

    /// <summary>خطای موقت پس از پایان تلاش‌های خودکار؛ نیازمند تلاش مجدد دستی.</summary>
    Failed = 6,

    /// <summary>خطای دائمی مانند شماره نامعتبر؛ تلاش خودکار متوقف است.</summary>
    PermanentFailure = 7,

    /// <summary>پیش از ارسال لغو شده است.</summary>
    Cancelled = 8,
}

public static class SmsQueueStatusRules
{
    /// <summary>وضعیت‌هایی که پیام در آن‌ها دیگر نباید خودکار ارسال شود.</summary>
    public static bool IsTerminal(this SmsQueueStatus status) =>
        status is SmsQueueStatus.Delivered or SmsQueueStatus.PermanentFailure or SmsQueueStatus.Cancelled;

    /// <summary>وضعیت‌هایی که نشان می‌دهند پیامک هنوز نتیجه نهایی ندارد و باید پیگیری تحویل شود.</summary>
    public static bool IsAwaitingDelivery(this SmsQueueStatus status) =>
        status is SmsQueueStatus.Accepted;
}

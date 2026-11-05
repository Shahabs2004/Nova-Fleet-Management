using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Notifications;

/// <summary>
/// قالب متن پیامک هر رخداد. متن در زمان ارسال از روی قالب ساخته می‌شود
/// و تغییر قالب روی پیام‌های قبلی اثر ندارد.
/// </summary>
public sealed class SmsTemplate : Entity
{
    private SmsTemplate()
    {
    }

    public SmsEventType EventType { get; private set; }

    public string EventKey { get; private set; } = string.Empty;

    /// <summary>متن قالب با توکن‌هایی مانند <c>{driver}</c> و <c>{missionNumber}</c>.</summary>
    public string Body { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public SmsTemplate(SmsEventType eventType, string body, bool isActive = true)
    {
        if (!Enum.IsDefined(eventType))
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), "نوع رخداد پیامک معتبر نیست.");
        }

        EventType = eventType;
        EventKey = SmsEventNames.Of(eventType);
        Update(body, isActive);
    }

    public void Update(string body, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body, nameof(body));
        var normalized = body.Trim();
        if (normalized.Length > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(body), "حداکثر طول قالب پیامک ۱۰۰۰ نویسه است.");
        }

        Body = normalized;
        IsActive = isActive;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

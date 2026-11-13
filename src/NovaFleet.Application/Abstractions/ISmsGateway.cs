namespace NovaFleet.Application.Abstractions;

/// <summary>پیام خروجی به سرویس‌دهنده پیامک.</summary>
public sealed record SmsOutboundMessage(string Mobile, string Body, string? CorrelationId);

/// <summary>نتیجه ارسال به سرویس‌دهنده؛ «پذیرش» با «تحویل» یکی نیست.</summary>
public sealed record SmsSendResult(
    bool Accepted,
    string? ProviderReference,
    string? Error,
    bool IsPermanentFailure,
    string? RawStatus)
{
    public static SmsSendResult Success(string? reference, string? rawStatus) => new(true, reference, null, false, rawStatus);

    public static SmsSendResult Transient(string error, string? rawStatus = null) => new(false, null, error, false, rawStatus);

    public static SmsSendResult Permanent(string error, string? rawStatus = null) => new(false, null, error, true, rawStatus);
}

public enum SmsDeliveryState
{
    /// <summary>نتیجه قطعی نیست یا پاسخ ناشناخته است؛ پیام در انتظار پیگیری می‌ماند.</summary>
    Unknown = 1,
    Delivered = 2,
    Failed = 3,
}

public sealed record SmsDeliveryResult(SmsDeliveryState State, string? RawStatus);

/// <summary>میزان اعتبار حساب در سرویس‌دهنده؛ رشته خام چون واحد و دقتش را سرویس‌دهنده تعیین می‌کند.</summary>
public sealed record SmsBalanceResult(bool Success, string? RawCredit, string? Error)
{
    public static SmsBalanceResult Of(string credit) => new(true, credit, null);

    public static SmsBalanceResult Failure(string error) => new(false, null, error);
}

/// <summary>
/// درگاه پیامک. حالت کار (شبیه‌سازی یا سرویس‌دهنده واقعی) بیرون از خود درگاه و از تنظیمات
/// پایگاه داده انتخاب می‌شود، پس هیچ عضو همگامی روی آن نیست که خواندن تنظیمات را بلوکه کند.
/// </summary>
public interface ISmsGateway
{
    Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default);

    Task<SmsDeliveryResult> GetDeliveryStatusAsync(string providerReference, CancellationToken cancellationToken = default);

    Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default);
}

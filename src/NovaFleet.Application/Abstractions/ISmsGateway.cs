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

/// <summary>درگاه پیامک ADP؛ پیاده‌سازی شبیه‌سازی نیز همین قرارداد را رعایت می‌کند.</summary>
public interface ISmsGateway
{
    bool IsSimulation { get; }

    Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default);

    Task<SmsDeliveryResult> GetDeliveryStatusAsync(string providerReference, CancellationToken cancellationToken = default);
}

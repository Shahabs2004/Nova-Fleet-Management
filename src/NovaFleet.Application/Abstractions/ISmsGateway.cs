namespace NovaFleet.Application.Abstractions;

/// <summary>پیام خروجی به سرویس‌دهنده پیامک.</summary>
public sealed record SmsOutboundMessage(string Mobile, string Body, string? CorrelationId);

/// <summary>نتیجه ارسال به سرویس‌دهنده؛ «پذیرش» با «تحویل» یکی نیست.</summary>
public sealed record SmsSendResult(
    bool Accepted,
    string? ProviderReference,
    string? Error,
    bool IsPermanentFailure,
    string? RawStatus,
    bool IsIndeterminate = false)
{
    public static SmsSendResult Success(string? reference, string? rawStatus) => new(true, reference, null, false, rawStatus);

    public static SmsSendResult Transient(string error, string? rawStatus = null) => new(false, null, error, false, rawStatus);

    public static SmsSendResult Permanent(string error, string? rawStatus = null) => new(false, null, error, true, rawStatus);

    /// <summary>ارسال یا نتیجه آن قابل قطعیت نیست؛ صف هرگز این مورد را خودکار دوباره ارسال نمی‌کند.</summary>
    public static SmsSendResult Indeterminate(string error, string? rawStatus = null) =>
        new(false, null, error, false, rawStatus, true);
}

/// <summary>حالت درگاهی که هنگام برداشت از صف ذخیره می‌شود و برای کل عمر پیام ثابت می‌ماند.</summary>
public enum SmsGatewayMode
{
    Simulation = 0,
    Wsdl = 1,
    Api = 2,
}

/// <summary>انتخاب درگاه ثابت بر اساس حالت ذخیره‌شده، مستقل از تنظیمات جاری.</summary>
public interface ISmsGatewaySelector
{
    ISmsGateway ForMode(SmsGatewayMode mode);
}

public static class SmsGatewayModes
{
    /// <summary>حالت ناشناخته عمداً به شبیه‌سازی می‌رود تا غلط تایپی هزینه‌ای ایجاد نکند.</summary>
    public static SmsGatewayMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "wsdl" => SmsGatewayMode.Wsdl,
        "api" => SmsGatewayMode.Api,
        _ => SmsGatewayMode.Simulation,
    };
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

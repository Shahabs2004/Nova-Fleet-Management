namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// تنظیمات درگاه پیامک. اطلاعات اتصال واقعی فقط از تنظیمات محافظت‌شده محیط خوانده می‌شود
/// و در مخزن عمومی قرار نمی‌گیرد. حالت پیش‌فرض شبیه‌سازی است.
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Simulation یا Adp.</summary>
    public string Mode { get; set; } = "Simulation";

    public string? Endpoint { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? SenderNumber { get; set; }

    public string SendAction { get; set; } = "http://tempuri.org/SendSms";

    public string StatusAction { get; set; } = "http://tempuri.org/GetDeliveryStatus";

    public int TimeoutSeconds { get; set; } = 20;

    public bool IsSimulation => !string.Equals(Mode, "Adp", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(Endpoint);
}

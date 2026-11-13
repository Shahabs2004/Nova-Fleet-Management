using Microsoft.Extensions.Logging;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// درگاه پیامک از ردیف `sms.mode` در پایگاه داده انتخاب می‌شود، پس تغییر حالت از تب تنظیمات
/// بی‌درنگ اعمال می‌شود و لازم نیست نسخه‌ی درگاه هنگام ساخت سرویس‌ها مشخص باشد.
/// </summary>
public sealed class SmsGatewayRouter(
    IRuntimeSettings settings,
    SimulatedSmsGateway simulated,
    PayamSmsGateway payam,
    ILogger<SmsGatewayRouter> logger) : ISmsGateway
{
    /// <summary>پیام صریح برای حالتی که قرارداد سرویس‌دهنده‌اش در این نصب پیاده نشده است.</summary>
    public const string UnimplementedModeMessage =
        "برای حالت انتخاب‌شده هنوز درگاه پیامک پیاده‌سازی نشده است؛ برای ارسال واقعی حالت Wsdl را انتخاب کنید.";

    public async Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
    {
        switch (await ReadModeAsync(cancellationToken))
        {
            case SmsGatewayMode.Simulation:
                return await simulated.SendAsync(message, cancellationToken);
            case SmsGatewayMode.Wsdl:
                return await payam.SendAsync(message, cancellationToken);
            default:
                return Refuse();
        }
    }

    public async Task<SmsDeliveryResult> GetDeliveryStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
        => await ReadModeAsync(cancellationToken) == SmsGatewayMode.Wsdl
            ? await payam.GetDeliveryStatusAsync(providerReference, cancellationToken)
            : await simulated.GetDeliveryStatusAsync(providerReference, cancellationToken);

    public async Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
        => await ReadModeAsync(cancellationToken) == SmsGatewayMode.Wsdl
            ? await payam.GetBalanceAsync(cancellationToken)
            : await simulated.GetBalanceAsync(cancellationToken);

    private SmsSendResult Refuse()
    {
        logger.LogWarning("{Message}", UnimplementedModeMessage);
        return SmsSendResult.Permanent(UnimplementedModeMessage);
    }

    private async Task<SmsGatewayMode> ReadModeAsync(CancellationToken cancellationToken)
    {
        var mode = (await settings.GetStringAsync(SettingKeys.SmsMode, cancellationToken))?.Trim();
        return mode?.ToLowerInvariant() switch
        {
            "wsdl" => SmsGatewayMode.Wsdl,
            "api" => SmsGatewayMode.Api,
            // نبودِ مقدار و هر مقدار ناشناخته به شبیه‌سازی می‌رود: هیچ پیامک پولی با یک تایپو ارسال نشود.
            _ => SmsGatewayMode.Simulation
        };
    }

    /// <summary>نام‌های مجاز `sms.mode` همان مقادیر این شمارش‌اند.</summary>
    public enum SmsGatewayMode
    {
        Simulation = 0,
        Wsdl = 1,
        Api = 2,
    }
}

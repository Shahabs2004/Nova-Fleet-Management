using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// درگاه پیامک از ردیف `sms.mode` در پایگاه داده انتخاب میشود، پس تغییر حالت از تب تنظیمات
/// بیدرنگ اعمال میشود و لازم نیست نسخهی درگاه هنگام ساخت سرویسها مشخص باشد.
/// </summary>
public sealed class SmsGatewayRouter(
    IRuntimeSettings settings,
    SimulatedSmsGateway simulated,
    PayamSmsGateway payam,
    PayamSmsRestGateway rest) : ISmsGateway
{
    public async Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
    {
        switch (await ReadModeAsync(cancellationToken))
        {
            case SmsGatewayMode.Wsdl:
                return await payam.SendAsync(message, cancellationToken);
            case SmsGatewayMode.Api:
                return await rest.SendAsync(message, cancellationToken);
            default:
                return await simulated.SendAsync(message, cancellationToken);
        }
    }

    public async Task<SmsDeliveryResult> GetDeliveryStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        switch (await ReadModeAsync(cancellationToken))
        {
            case SmsGatewayMode.Wsdl:
                return await payam.GetDeliveryStatusAsync(providerReference, cancellationToken);
            case SmsGatewayMode.Api:
                return await rest.GetDeliveryStatusAsync(providerReference, cancellationToken);
            default:
                return await simulated.GetDeliveryStatusAsync(providerReference, cancellationToken);
        }
    }

    public async Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        switch (await ReadModeAsync(cancellationToken))
        {
            case SmsGatewayMode.Wsdl:
                return await payam.GetBalanceAsync(cancellationToken);
            case SmsGatewayMode.Api:
                return await rest.GetBalanceAsync(cancellationToken);
            default:
                return await simulated.GetBalanceAsync(cancellationToken);
        }
    }

    private async Task<SmsGatewayMode> ReadModeAsync(CancellationToken cancellationToken)
    {
        var mode = (await settings.GetStringAsync(SettingKeys.SmsMode, cancellationToken))?.Trim();
        return mode?.ToLowerInvariant() switch
        {
            "wsdl" => SmsGatewayMode.Wsdl,
            "api" => SmsGatewayMode.Api,
            // نبودِ مقدار و هر مقدار ناشناخته به شبیهسازی میرود: هیچ پیامک پولی با یک تایپو ارسال نشود.
            _ => SmsGatewayMode.Simulation
        };
    }

    /// <summary>نامهای مجاز `sms.mode` همان مقادیر این شمارشاند.</summary>
    public enum SmsGatewayMode
    {
        Simulation = 0,
        Wsdl = 1,
        Api = 2,
    }
}
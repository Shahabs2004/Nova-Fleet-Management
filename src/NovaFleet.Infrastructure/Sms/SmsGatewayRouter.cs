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
    PayamSmsRestGateway rest) : ISmsGateway, ISmsGatewaySelector
{
    public async Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
    {
        var selected = ForMode(await ReadModeAsync(cancellationToken));
        return await selected.SendAsync(message, cancellationToken);
    }

    public async Task<SmsDeliveryResult> GetDeliveryStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        var selected = ForMode(await ReadModeAsync(cancellationToken));
        return await selected.GetDeliveryStatusAsync(providerReference, cancellationToken);
    }

    public async Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var selected = ForMode(await ReadModeAsync(cancellationToken));
        return await selected.GetBalanceAsync(cancellationToken);
    }

    public ISmsGateway ForMode(SmsGatewayMode mode) => mode switch
    {
        SmsGatewayMode.Wsdl => payam,
        SmsGatewayMode.Api => rest,
        // نبودِ مقدار و هر مقدار ناشناخته به شبیهسازی میرود: هیچ پیامک پولی با یک تایپو ارسال نشود.
        _ => simulated,
    };

    private async Task<SmsGatewayMode> ReadModeAsync(CancellationToken cancellationToken)
    {
        var mode = await settings.GetStringAsync(SettingKeys.SmsMode, cancellationToken);
        return SmsGatewayModes.Parse(mode);
    }
}
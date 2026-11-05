using Microsoft.Extensions.Options;
using NovaFleet.Application.Abstractions;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// درگاه شبیه‌سازی؛ همان قرارداد درگاه واقعی را دارد و برای توسعه و آزمون استفاده می‌شود.
/// هر پیام پذیرفته و در نخستین استعلام، تحویل‌شده گزارش می‌شود.
/// </summary>
public sealed class SimulatedSmsGateway(IOptions<SmsOptions> options) : ISmsGateway
{
    private readonly SmsOptions _options = options.Value;

    public bool IsSimulation => true;

    public Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
    {
        if (_options.Mode.Equals("Adp", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(SmsSendResult.Transient("تنظیمات درگاه ADP کامل نیست؛ پیام در صف می‌ماند."));
        }

        var reference = $"SIM-{Guid.NewGuid():N}"[..16];
        return Task.FromResult(SmsSendResult.Success(reference, "SimulatedAccepted"));
    }

    public Task<SmsDeliveryResult> GetDeliveryStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SmsDeliveryResult(SmsDeliveryState.Delivered, "SimulatedDelivered"));
}

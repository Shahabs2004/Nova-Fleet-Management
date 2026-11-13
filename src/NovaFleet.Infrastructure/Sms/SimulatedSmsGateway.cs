using NovaFleet.Application.Abstractions;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// درگاه شبیه‌سازی؛ همان قرارداد درگاه واقعی را دارد و برای توسعه و آزمون استفاده می‌شود.
/// هر پیام پذیرفته، در نخستین استعلام تحویل‌شده و اعتبار حساب ساختگی گزارش می‌شود.
/// </summary>
public sealed class SimulatedSmsGateway : ISmsGateway
{
    /// <summary>اعتبار ساختگی به ریال؛ فقط برای اینکه تست اتصال در حالت شبیه‌سازی هم جواب بدهد.</summary>
    public const string SimulatedCreditRial = "5000000";

    public Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
        => Task.FromResult(SmsSendResult.Success($"SIM-{Guid.NewGuid():N}"[..16], "SimulatedAccepted"));

    public Task<SmsDeliveryResult> GetDeliveryStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SmsDeliveryResult(SmsDeliveryState.Delivered, "SimulatedDelivered"));

    public Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SmsBalanceResult.Of(SimulatedCreditRial));
}

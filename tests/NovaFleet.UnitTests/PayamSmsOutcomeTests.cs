using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Sms;

/// <summary>
/// پاسخ‌های مبهم payamSMS نباید به تلاش خودکار دوباره تبدیل شوند و هیچ بدنه خام حساسی
/// نباید در خطا یا لاگ قرار بگیرد. تمام فراخوان‌ها با HTTP ساختگی انجام می‌شوند.
/// </summary>
public sealed class PayamSmsOutcomeTests : MissionTestBase
{
    private const string Endpoint = "https://sms.invalid/services";
    private const string SecretMarker = "do-not-store-this-secret";
    private static readonly SmsOutboundMessage Message = new("09121112233", "متن آزمون", null);

    [Fact]
    public async Task SoapTimeout_IsIndeterminateAndNeverSuggestsAutomaticRetry()
    {
        var handler = new StubHttpMessageHandler(new TaskCanceledException("timeout after provider acceptance"));
        var gateway = Gateway(handler, rest: false);
        await ConfigureConnectionAsync();

        var result = await gateway.SendAsync(Message);

        Assert.True(result.IsIndeterminate);
        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Null(result.ProviderReference);
    }

    [Fact]
    public async Task RestTimeout_IsIndeterminateAndNeverSuggestsAutomaticRetry()
    {
        var handler = new StubHttpMessageHandler(new TaskCanceledException("timeout after provider acceptance"));
        var gateway = Gateway(handler, rest: true);
        await ConfigureConnectionAsync();

        var result = await gateway.SendAsync(Message);

        Assert.True(result.IsIndeterminate);
        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Null(result.ProviderReference);
    }

    [Fact]
    public async Task SoapUnreadableResponse_IsIndeterminateWithoutExposingTheBody()
    {
        var handler = new StubHttpMessageHandler($"<not-xml>{SecretMarker}</not-xml>");
        var logger = new RecordingLogger();
        var gateway = Gateway(handler, rest: false, logger);
        await ConfigureConnectionAsync();

        var result = await gateway.SendAsync(Message);

        Assert.True(result.IsIndeterminate);
        Assert.DoesNotContain(SecretMarker, result.Error + result.RawStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(SecretMarker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestUnreadableResponse_IsIndeterminateWithoutExposingTheBody()
    {
        var handler = new StubHttpMessageHandler($"{{\"password\":\"{SecretMarker}\"");
        var logger = new RecordingLogger();
        var gateway = Gateway(handler, rest: true, logger);
        await ConfigureConnectionAsync();

        var result = await gateway.SendAsync(Message);

        Assert.True(result.IsIndeterminate);
        Assert.DoesNotContain(SecretMarker, result.Error + result.RawStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(SecretMarker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AcceptedResponseWithoutUsableReference_IsIndeterminate()
    {
        var handler = new StubHttpMessageHandler(
            """{"code":200,"message":"accepted","data":[{"serverId":" ","mobile":"09121112233"}]}""");
        var gateway = Gateway(handler, rest: true);
        await ConfigureConnectionAsync();

        var result = await gateway.SendAsync(Message);

        Assert.True(result.IsIndeterminate);
        Assert.False(result.Accepted);
        Assert.Null(result.ProviderReference);
    }

    [Fact]
    public async Task RestUnmappedSuccess_DoesNotLogRawDataContainingASecret()
    {
        var handler = new StubHttpMessageHandler(
            $"{{\"code\":200,\"message\":\"accepted\",\"data\":{{\"password\":\"{SecretMarker}\"}}}}");
        var logger = new RecordingLogger();
        var gateway = Gateway(handler, rest: true, logger);
        await ConfigureConnectionAsync();

        var result = await gateway.SendAsync(Message);

        Assert.True(result.IsIndeterminate);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(SecretMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(SecretMarker, result.Error + result.RawStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Router_StoredApiModeKeepsUsingRestAfterGlobalModeChangesToSimulation()
    {
        var soapHandler = new StubHttpMessageHandler(SoapSuccess());
        var restHandler = new StubHttpMessageHandler(RestSuccess());
        var soapFactory = new StubHttpClientFactory(soapHandler);
        var restFactory = new StubHttpClientFactory(restHandler);
        var router = new SmsGatewayRouter(
            Runtime,
            new SimulatedSmsGateway(),
            new PayamSmsGateway(soapFactory, Runtime, NullLogger<PayamSmsGateway>.Instance),
            new PayamSmsRestGateway(restFactory, Runtime, NullLogger<PayamSmsRestGateway>.Instance));
        await ConfigureConnectionAsync();
        await Runtime.SetAsync(SettingKeys.SmsMode, "Api");
        var storedApiGateway = router.ForMode(SmsGatewayMode.Api);
        await Runtime.SetAsync(SettingKeys.SmsMode, "Simulation");

        var apiResult = await storedApiGateway.SendAsync(Message);
        var soapResult = await router.ForMode(SmsGatewayMode.Wsdl).SendAsync(Message);

        Assert.Equal("700123", apiResult.ProviderReference);
        Assert.Equal("700123", soapResult.ProviderReference);
        Assert.Equal(1, restHandler.CallCount);
        Assert.Equal(1, soapHandler.CallCount);
    }

    private ISmsGateway Gateway(StubHttpMessageHandler handler, bool rest, ILogger? logger = null)
    {
        var factory = new StubHttpClientFactory(handler);
        return rest
            ? new PayamSmsRestGateway(factory, Runtime, (logger as ILogger<PayamSmsRestGateway>) ?? NullLogger<PayamSmsRestGateway>.Instance)
            : new PayamSmsGateway(factory, Runtime, (logger as ILogger<PayamSmsGateway>) ?? NullLogger<PayamSmsGateway>.Instance);
    }

    private async Task ConfigureConnectionAsync()
    {
        await Runtime.SetAsync(SettingKeys.SmsEndpoint, Endpoint);
        await Runtime.SetAsync(SettingKeys.SmsOrganization, "ORG-1");
        await Runtime.SetAsync(SettingKeys.SmsUsername, "api-user");
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "api-pass");
        await Runtime.SetAsync(SettingKeys.SmsSenderNumber, "30001000");
    }

    private static string SoapSuccess() =>
        "<tns:SendResponse xmlns:tns=\"urn:SMSAPIwsdl\"><SendReturn><SendResult><ID>700123</ID>" +
        "</SendResult></SendReturn></tns:SendResponse>";

    private static string RestSuccess() =>
        """{"code":200,"message":"send result","data":[{"serverId":"700123"}]}""";

    private sealed class RecordingLogger : ILogger, ILogger<PayamSmsGateway>, ILogger<PayamSmsRestGateway>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}

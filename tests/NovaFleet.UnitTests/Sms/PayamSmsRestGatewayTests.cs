using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Sms;

/// <summary>
/// درگاه REST payamSMS با handler ساختگی سنجیده می‌شود: شکل JSON مطابق پروسیجر مرجع SP_SendSMS،
/// نگاشت کدهای خطای سند SOAP، و اعتبارسنجی getbalance برای آزمون اتصال.
/// </summary>
public sealed class PayamSmsRestGatewayTests : MissionTestBase
{
    private const string RestEndpoint = "https://www.payamsms.com/services/rest/index.php";

    private static readonly SmsOutboundMessage Message = new(
        "09121112233", "مأموریت شما تأیید شد & لطفاً کد ۱۲۳ را نگه دارید", null);

    private StubHttpMessageHandler handler = null!;
    private StubHttpClientFactory factory = null!;

    [Fact]
    public async Task Send_PostsTheStoredProcedureJsonContract()
    {
        var gateway = await WiredGatewayAsync(SendOk("700123"));

        await gateway.SendAsync(Message);

        var request = handler.Requests.Single();
        Assert.Equal(RestEndpoint, request.Url);
        Assert.Equal("application/json", request.MediaType);

        using var document = JsonDocument.Parse(request.Body!);
        var root = document.RootElement;
        Assert.Equal("ORG-1", root.GetProperty("organization").GetString());
        Assert.Equal("api-user", root.GetProperty("username").GetString());
        // رمز از انبار اسرار خوانده می‌شود، نه از ستونِ خواندنیِ عمومی.
        Assert.Equal("api-pass", root.GetProperty("password").GetString());
        Assert.Equal("send", root.GetProperty("method").GetString());

        var message = root.GetProperty("messages").EnumerateArray().Single();
        Assert.Equal("30001000", message.GetProperty("sender").GetString());
        Assert.Equal("989121112233", message.GetProperty("recipient").GetString());
        Assert.Equal(Message.Body, message.GetProperty("body").GetString());
        Assert.Equal(1, message.GetProperty("customerId").GetInt32());
    }

    [Fact]
    public async Task Send_SendsThePersianBodyAsRawUtf8()
    {
        var gateway = await WiredGatewayAsync(SendOk("700123"));

        await gateway.SendAsync(Message);

        var request = handler.Requests.Single();
        Assert.Contains("تأیید شد", request.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", request.Body!, StringComparison.Ordinal);
        Assert.True(ContainsBytes(request.Raw, Encoding.UTF8.GetBytes("تأیید شد")));
    }

    [Fact]
    public async Task Send_ReturnsTheServerMessageIdAsProviderReference()
    {
        var gateway = await WiredGatewayAsync(SendOk("700123"));

        var result = await gateway.SendAsync(Message);

        Assert.True(result.Accepted);
        Assert.Equal("700123", result.ProviderReference);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Send_ErrorCodeItemBecomesAPermanentFailure()
    {
        var gateway = await WiredGatewayAsync(SendError("E5"));

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.True(result.IsPermanentFailure);
        Assert.Contains("E5", result.Error, StringComparison.Ordinal);
        Assert.Contains("شناسه کاربری یا رمز", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("E6", false)] // اعتبار تمام شده: تلاش دوباره بعد از شارژ معنا دارد.
    [InlineData("E43", true)]
    [InlineData("E223", true)]
    [InlineData("E999", true)]
    public async Task Send_KnownFaultCodesMapToTheirDocumentedSeverity(string code, bool permanent)
    {
        var gateway = await WiredGatewayAsync(SendError(code));

        var result = await gateway.SendAsync(Message);

        Assert.Equal(permanent, result.IsPermanentFailure);
        Assert.Contains(code, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_UnknownFaultCodeStaysTransient()
    {
        var gateway = await WiredGatewayAsync(SendError("E4041"));

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Contains("خطای نامشخص", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_UnknownMethodCodeIsPermanentBecauseRetryingCannotFixIt()
    {
        var gateway = await WiredGatewayAsync("""{"code":111,"message":"check parameters {method}"}""");

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.True(result.IsPermanentFailure);
        Assert.Contains("۱۱۱", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_Non200OuterCodeIsTransient()
    {
        var gateway = await WiredGatewayAsync("""{"code":503,"message":"service unavailable"}""");

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Contains("503", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_MissingIdIsTransient()
    {
        var gateway = await WiredGatewayAsync("""{"code":200,"message":"send result","data":[{}]}""");

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.Contains("شناسه پیام برنگرداند", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_MissingDataIsTransient()
    {
        var gateway = await WiredGatewayAsync("""{"code":200,"message":"send result"}""");

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.Contains("شناسه پیام برنگرداند", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_TransportFailureIsRetryable()
    {
        handler = new StubHttpMessageHandler(new HttpRequestException("connection refused"));
        var gateway = Gateway();
        await ConfigureConnectionAsync();

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Contains("ارتباط با سرویس پیامک برقرار نشد", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_HttpErrorWithoutJsonNamesTheStatusCode()
    {
        var gateway = await WiredGatewayAsync("<html>gateway is down</html>", HttpStatusCode.ServiceUnavailable);

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Contains("503", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_UnparseableBodyIsTransient()
    {
        var gateway = await WiredGatewayAsync("not json at all");

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Contains("قابل‌خواندن نیست", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_IncompleteConnectionNamesEveryMissingPartAndNeverCallsTheProvider()
    {
        handler = new StubHttpMessageHandler(SendOk("700123"));
        var gateway = Gateway();
        await Runtime.SetAsync(SettingKeys.SmsEndpoint, RestEndpoint);
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "api-pass");

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.Contains("سازمان", result.Error, StringComparison.Ordinal);
        Assert.Contains("شناسه کاربری", result.Error, StringComparison.Ordinal);
        Assert.Contains("شماره فرستنده", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("رمز", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Send_EmptyEndpointFallsBackToTheRestDefault()
    {
        handler = new StubHttpMessageHandler(SendOk("700123"));
        var gateway = Gateway();
        await Runtime.SetAsync(SettingKeys.SmsOrganization, "ORG-1");
        await Runtime.SetAsync(SettingKeys.SmsUsername, "api-user");
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "api-pass");
        await Runtime.SetAsync(SettingKeys.SmsSenderNumber, "30001000");

        var result = await gateway.SendAsync(Message);

        Assert.True(result.Accepted);
        Assert.Equal(PayamSmsRestGateway.DefaultEndpoint, handler.Requests.Single().Url);
    }

    [Fact]
    public async Task Send_RejectsAMobileThatCannotBecomeCountryPrefixedWithoutCallingTheProvider()
    {
        var gateway = await WiredGatewayAsync(SendOk("700123"));

        var result = await gateway.SendAsync(new SmsOutboundMessage("0912", "متن", null));

        Assert.False(result.Accepted);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Balance_PostsGetBalanceAndReturnsTheRawCredit()
    {
        var gateway = await WiredGatewayAsync(BalanceOk("12500"));

        var result = await gateway.GetBalanceAsync();

        using var document = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.Equal("getbalance", document.RootElement.GetProperty("method").GetString());
        Assert.False(document.RootElement.TryGetProperty("messages", out _));
        Assert.True(result.Success);
        Assert.Equal("12500", result.RawCredit);
    }

    [Fact]
    public async Task Balance_NumericDataIsReturnedAsText()
    {
        var gateway = await WiredGatewayAsync("""{"code":200,"message":"send result","data":12500}""");

        var result = await gateway.GetBalanceAsync();

        Assert.True(result.Success);
        Assert.Equal("12500", result.RawCredit);
    }

    [Fact]
    public async Task Balance_FaultCodeBecomesAFailureMessage()
    {
        var gateway = await WiredGatewayAsync(BalanceOk("E4"));

        var result = await gateway.GetBalanceAsync();

        Assert.False(result.Success);
        Assert.Contains("دسترسی به سرویس پیامک مجاز نیست", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Balance_IncompleteConnectionNeverCallsTheProvider()
    {
        handler = new StubHttpMessageHandler(BalanceOk("1"));
        var gateway = Gateway();

        var result = await gateway.GetBalanceAsync();

        Assert.False(result.Success);
        Assert.Contains("کامل نیست", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DeliveryStatus_IsUnknownWithoutCallingTheProvider()
    {
        var gateway = await WiredGatewayAsync(SendOk("700123"));

        var result = await gateway.GetDeliveryStatusAsync("700123");

        Assert.Equal(SmsDeliveryState.Unknown, result.State);
        Assert.Contains("متد وضعیت تحویل ندارد", result.RawStatus, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task TimeoutSeconds_ComesFromTheDatabaseAndStaysInsideSaneBounds()
    {
        handler = new StubHttpMessageHandler(SendOk("700123"));
        var gateway = Gateway();
        await ConfigureConnectionAsync();

        await Runtime.SetAsync(SettingKeys.SmsTimeoutSeconds, "45");
        await gateway.SendAsync(Message);
        Assert.Equal(TimeSpan.FromSeconds(45), factory.LastClient!.Timeout);

        await Runtime.SetAsync(SettingKeys.SmsTimeoutSeconds, "9999");
        await gateway.SendAsync(Message);
        Assert.Equal(120, factory.LastClient!.Timeout.TotalSeconds);
    }

    private async Task<PayamSmsRestGateway> WiredGatewayAsync(
        string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        handler = new StubHttpMessageHandler(responseBody, status);
        var gateway = Gateway();
        await ConfigureConnectionAsync();
        return gateway;
    }

    private PayamSmsRestGateway Gateway()
    {
        factory = new StubHttpClientFactory(handler);
        return new PayamSmsRestGateway(factory, Runtime, NullLogger<PayamSmsRestGateway>.Instance);
    }

    private async Task ConfigureConnectionAsync()
    {
        await Runtime.SetAsync(SettingKeys.SmsEndpoint, RestEndpoint);
        await Runtime.SetAsync(SettingKeys.SmsOrganization, "ORG-1");
        await Runtime.SetAsync(SettingKeys.SmsUsername, "api-user");
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "api-pass");
        await Runtime.SetAsync(SettingKeys.SmsSenderNumber, "30001000");
    }

    private static string SendOk(string id) => JsonSerializer.Serialize(new
    {
        code = 200,
        message = "send result",
        data = new[] { new { ID = id, Mobile = "989121112233" } }
    });

    private static string SendError(string errorCode) => JsonSerializer.Serialize(new
    {
        code = 200,
        message = "send result",
        data = new[] { new { ErrorCode = errorCode } }
    });

    private static string BalanceOk(string credit) => JsonSerializer.Serialize(new
    {
        code = 200,
        message = "send result",
        data = credit
    });

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        for (var offset = 0; offset <= haystack.Length - needle.Length; offset++)
        {
            if (haystack.AsSpan(offset, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}
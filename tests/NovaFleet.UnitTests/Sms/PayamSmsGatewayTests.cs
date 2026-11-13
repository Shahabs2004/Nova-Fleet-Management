using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Sms;

/// <summary>
/// درگاه payamSMS با یک HttpMessageHandler ساختگی سنجیده می‌شود: قرارداد سیم (پاکت، SOAPAction،
/// ترتیب و نوع پارامترها)، طبقه‌بندی خطاهای سند، و انتخاب حالت از تنظیمات پایگاه داده.
/// </summary>
public sealed class PayamSmsGatewayTests : MissionTestBase
{
    private const string Endpoint = "https://www.payamsms.com/services/v2/index.php";
    private const string SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string EncNs = "http://schemas.xmlsoap.org/soap/encoding/";
    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly SmsOutboundMessage Message = new(
        "09121112233", "مأموریت شما تأیید شد & لطفاً کد ۱۲۳ را نگه دارید", null);

    private StubHttpMessageHandler handler = null!;
    private StubHttpClientFactory factory = null!;

    [Fact]
    public async Task Send_PostsRpcEncodedEnvelopeWithTheWsdlParameterOrder()
    {
        var gateway = await WiredGatewayAsync("<tns:SendResponse xmlns:tns=\"urn:SMSAPIwsdl\">" +
            "<SendReturn><SendResult><ID>700123</ID><Credit>940</Credit></SendResult></SendReturn>" +
            "</tns:SendResponse>");

        await gateway.SendAsync(Message);

        var request = handler.Requests.Single();
        Assert.Equal(Endpoint, request.Url);
        Assert.Contains("urn:SMSAPIwsdl#Send", request.SoapAction!, StringComparison.Ordinal);
        Assert.Equal("text/xml", request.MediaType);

        var document = XDocument.Parse(request.Body!);
        var envelope = document.Root!;
        Assert.Equal(SoapNs, envelope.Name.NamespaceName);
        Assert.Equal(EncNs, (string?)envelope.Attribute(Xns(SoapNs) + "encodingStyle"));

        // چهار پیشوند لازمِ سبک encoded باید در خود ریشه اعلام شده باشند، وگرنه
        // QNameهای داخل xsi:type و arrayType در سمت سرور PHP حل نمی‌شوند.
        foreach (var prefix in new[] { "xsi", "xsd", "SOAP-ENC", "tns" })
        {
            Assert.NotNull(envelope.GetNamespaceOfPrefix(prefix));
        }

        var operation = envelope.Element(Xns(SoapNs) + "Body")!.Elements().Single();
        Assert.Equal("Send", operation.Name.LocalName);
        Assert.Equal("urn:SMSAPIwsdl", operation.Name.NamespaceName);
        Assert.Equal("organization,username,password,srcNumber,body,destNo",
            string.Join(",", operation.Elements().Select(element => element.Name.LocalName)));

        // پارامترهای سبک RPC عمداً بی‌namespace می‌مانند.
        Assert.All(operation.Elements(), element => Assert.Equal(string.Empty, element.Name.NamespaceName));

        Assert.Equal("ORG-1", Value(operation, "organization"));
        Assert.Equal("api-user", Value(operation, "username"));
        Assert.Equal("30001000", Value(operation, "srcNumber"));

        // رمز از انبار اسرار خوانده می‌شود، نه از ستونِ خواندنیِ عمومی.
        Assert.Equal("api-pass", Value(operation, "password"));

        var destNo = operation.Elements().Single(element => element.Name.LocalName == "destNo");
        Assert.Equal("SOAP-ENC:Array", (string?)destNo.Attribute(Xns(XsiNs) + "type"));
        Assert.Equal("xsd:string[1]", (string?)destNo.Attribute(Xns(EncNs) + "arrayType"));
        Assert.Equal("989121112233", destNo.Elements().Single().Value);
    }

    [Fact]
    public async Task Send_SendsThePersianBodyAsUtf8AndEscapesMarkup()
    {
        var gateway = await WiredGatewayAsync(SendResponse("700123"));

        await gateway.SendAsync(Message);

        var request = handler.Requests.Single();
        Assert.Contains("تأیید شد", request.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("&#", request.Body!, StringComparison.Ordinal);
        Assert.Contains("&amp;", request.Body!, StringComparison.Ordinal);

        // همان بایت‌ها باید UTF-8 باشند، نه فرارِ کاراکترها به شکل &#1575;.
        Assert.True(ContainsBytes(request.Raw, Encoding.UTF8.GetBytes("تأیید شد")));
    }

    [Fact]
    public async Task Send_ReturnsTheServerMessageIdAsProviderReference()
    {
        var gateway = await WiredGatewayAsync(SendResponse("700123"));

        var result = await gateway.SendAsync(Message);

        Assert.True(result.Accepted);
        Assert.Equal("700123", result.ProviderReference);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Send_SingleRowErrorCodeBecomesAPermanentFailure()
    {
        var gateway = await WiredGatewayAsync(SendResponse("E43 Recipient Number is not Valid"));

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.True(result.IsPermanentFailure);
        Assert.Contains("E43", result.Error, StringComparison.Ordinal);
        Assert.Contains("شماره گیرنده نامعتبر", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("E6", false)] // اعتبار تمام شده: تلاش دوباره بعد از شارژ معنا دارد.
    [InlineData("E5", true)]
    [InlineData("E107", true)]
    [InlineData("E223", true)]
    [InlineData("E999", true)]
    public async Task Send_KnownFaultCodesMapToTheirDocumentedSeverity(string code, bool permanent)
    {
        var gateway = await WiredGatewayAsync(SendResponse(code));

        var result = await gateway.SendAsync(Message);

        Assert.Equal(permanent, result.IsPermanentFailure);
        Assert.Contains(code, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_UnknownFaultCodeStaysTransient()
    {
        var gateway = await WiredGatewayAsync(SendResponse("E4041 Something new"));

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Contains("خطای نامشخص", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_SoapFaultOverHttp500IsStillClassified()
    {
        var gateway = await WiredGatewayAsync(
            "<soap:Envelope xmlns:soap=\"" + SoapNs + "\"><soap:Body><soap:Fault>" +
            "<faultcode>soap:Server</faultcode><faultstring>E5 username or password is wrong</faultstring>" +
            "</soap:Fault></soap:Body></soap:Envelope>",
            HttpStatusCode.InternalServerError);

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.True(result.IsPermanentFailure);
        Assert.Contains("شناسه کاربری یا رمز", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_HttpErrorWithoutFaultNamesTheStatusCode()
    {
        var gateway = await WiredGatewayAsync("<html>gateway is down</html>", HttpStatusCode.ServiceUnavailable);

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.False(result.IsPermanentFailure);
        Assert.Contains("503", result.Error, StringComparison.Ordinal);
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
    public async Task Send_MissingSendResultIdIsTransient()
    {
        var gateway = await WiredGatewayAsync("<tns:SendResponse xmlns:tns=\"urn:SMSAPIwsdl\">" +
            "<SendReturn><SendResult><Status>queued</Status></SendResult></SendReturn></tns:SendResponse>");

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.Contains("شناسه پیام برنگرداند", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_IncompleteConnectionNamesEveryMissingPartAndNeverCallsTheProvider()
    {
        handler = new StubHttpMessageHandler(SendResponse("700123"));
        var gateway = Gateway();
        await Runtime.SetAsync(SettingKeys.SmsEndpoint, Endpoint);
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "api-pass");

        var result = await gateway.SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.Contains("شناسه کاربری", result.Error, StringComparison.Ordinal);
        Assert.Contains("شماره فرستنده", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("آدرس سرویس", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("رمز", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Send_RejectsAMobileThatCannotBecomeCountryPrefixedWithoutCallingTheProvider()
    {
        var gateway = await WiredGatewayAsync(SendResponse("700123"));

        var result = await gateway.SendAsync(new SmsOutboundMessage("0912", "متن", null));

        Assert.False(result.Accepted);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("09121112233", "989121112233")]
    [InlineData("9121112233", "989121112233")]
    [InlineData("00989121112233", "989121112233")]
    [InlineData("+98 912 111 2233", "989121112233")]
    [InlineData("989121112233", "989121112233")]
    [InlineData("0912111223", "98912111223")]
    [InlineData("07112345678", "987112345678")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("12345", null)]
    [InlineData("09121112233445566", null)]
    [InlineData("071123456", null)]
    public void NormalizeDestination_AcceptsEveryDocumentedShapeAndNothingElse(string? input, string? expected)
        => Assert.Equal(expected, PayamSmsGateway.NormalizeDestination(input));

    [Fact]
    public async Task DeliveryStatus_PostsTheServerIdAsALongArrayUnderTheStatusAction()
    {
        var gateway = await WiredGatewayAsync("<tns:getStatuses_OriginalIDResponse xmlns:tns=\"urn:SMSAPIwsdl\">" +
            "<getStatuses_OriginalIDReturn><DeliveryResult><ID>700123</ID><Status>Delivered</Status>" +
            "</DeliveryResult></getStatuses_OriginalIDReturn></tns:getStatuses_OriginalIDResponse>");

        var result = await gateway.GetDeliveryStatusAsync("700123");

        var request = handler.Requests.Single();
        Assert.Contains("urn:SMSAPIwsdl#getStatuses", request.SoapAction!, StringComparison.Ordinal);
        var document = XDocument.Parse(request.Body!);
        var ids = document.Descendants().Single(element => element.Name.LocalName == "Ids");
        Assert.Equal("xsd:long[1]", (string?)ids.Attribute(Xns(EncNs) + "arrayType"));
        Assert.Equal("700123", ids.Elements().Single().Value);
        Assert.Equal(SmsDeliveryState.Delivered, result.State);
    }

    [Fact]
    public async Task DeliveryStatus_LeavesUnfamiliarProviderAnswersUnknown()
    {
        var gateway = await WiredGatewayAsync(SendStatus("Checked"));

        Assert.Equal(SmsDeliveryState.Unknown, (await gateway.GetDeliveryStatusAsync("700123")).State);
    }

    [Fact]
    public async Task DeliveryStatus_RejectsANonNumericReferenceWithoutCallingTheProvider()
    {
        var gateway = await WiredGatewayAsync(SendStatus("Delivered"));

        var result = await gateway.GetDeliveryStatusAsync("SIM-0f1e2d3c");

        Assert.Equal(SmsDeliveryState.Unknown, result.State);
        Assert.Contains("عددی نیست", result.RawStatus, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("Delivered", SmsDeliveryState.Delivered)]
    [InlineData("تحویل داده شد", SmsDeliveryState.Delivered)]
    [InlineData("Failed", SmsDeliveryState.Failed)]
    [InlineData("Undelivered", SmsDeliveryState.Failed)]
    [InlineData("عدم تحویل", SmsDeliveryState.Failed)]
    [InlineData("در انتظار", SmsDeliveryState.Unknown)]
    [InlineData("", SmsDeliveryState.Unknown)]
    [InlineData(null, SmsDeliveryState.Unknown)]
    public void MapDelivery_OnlyTreatsExplicitWordsAsFinal(string? raw, SmsDeliveryState expected)
        => Assert.Equal(expected, PayamSmsGateway.MapDelivery(raw).State);

    [Fact]
    public async Task Balance_UsesTheDocumentedSoapActionAndReturnsTheRawCredit()
    {
        var gateway = await WiredGatewayAsync("<tns:BalanceResponse xmlns:tns=\"urn:SMSAPIwsdl\">" +
            "<return>12500</return></tns:BalanceResponse>");

        var result = await gateway.GetBalanceAsync();

        Assert.Contains("urn:SMSAPIwsdl#return account balance", handler.Requests.Single().SoapAction!, StringComparison.Ordinal);
        Assert.True(result.Success);
        Assert.Equal("12500", result.RawCredit);
    }

    [Fact]
    public async Task Balance_FaultCodeBecomesAFailureMessage()
    {
        var gateway = await WiredGatewayAsync("<tns:BalanceResponse xmlns:tns=\"urn:SMSAPIwsdl\">" +
            "<return>E4 Access is not allowed</return></tns:BalanceResponse>");

        var result = await gateway.GetBalanceAsync();

        Assert.False(result.Success);
        Assert.Contains("دسترسی به سرویس پیامک مجاز نیست", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Balance_IncompleteConnectionNeverCallsTheProvider()
    {
        handler = new StubHttpMessageHandler(SendResponse("1"));
        var gateway = Gateway();

        var result = await gateway.GetBalanceAsync();

        Assert.False(result.Success);
        Assert.Contains("کامل نیست", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task TimeoutSeconds_ComesFromTheDatabaseAndStaysInsideSaneBounds()
    {
        handler = new StubHttpMessageHandler(SendResponse("700123"));
        var gateway = Gateway();
        await ConfigureConnectionAsync();

        await Runtime.SetAsync(SettingKeys.SmsTimeoutSeconds, "45");
        await gateway.SendAsync(Message);
        Assert.Equal(TimeSpan.FromSeconds(45), factory.LastClient!.Timeout);

        // مقداری بیرون از بازهٔ معقول، درون همان بازه محدود می‌شود.
        await Runtime.SetAsync(SettingKeys.SmsTimeoutSeconds, "9999");
        await gateway.SendAsync(Message);
        Assert.Equal(120, factory.LastClient!.Timeout.TotalSeconds);
    }

    [Fact]
    public async Task Router_SimulatesWhenNoModeIsStored()
    {
        handler = new StubHttpMessageHandler(SendResponse("700123"));
        var router = Router();

        var result = await router.SendAsync(Message);

        Assert.True(result.Accepted);
        Assert.StartsWith("SIM-", result.ProviderReference, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("Wsdl")]
    [InlineData("wsdl")]
    [InlineData(" WSDL ")]
    public async Task Router_WsdlModeCallsThePayamGateway(string mode) => await RouterCallsPayamAsync(mode);

    [Fact]
    public async Task Router_ApiModeRefusesAndExplainsHowToSendForReal()
    {
        handler = new StubHttpMessageHandler(SendResponse("700123"));
        await Runtime.SetAsync(SettingKeys.SmsMode, "Api");
        await ConfigureConnectionAsync();

        var result = await Router().SendAsync(Message);

        Assert.False(result.Accepted);
        Assert.True(result.IsPermanentFailure);
        Assert.Equal(SmsGatewayRouter.UnimplementedModeMessage, result.Error);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Router_UnknownModeFallsBackToSimulationSoATypoNeverCostsCredit()
    {
        handler = new StubHttpMessageHandler(SendResponse("700123"));
        await Runtime.SetAsync(SettingKeys.SmsMode, "Wsdll");

        Assert.True((await Router().SendAsync(Message)).Accepted);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Router_SwitchesModeWithoutRebuildingTheServices()
    {
        handler = new StubHttpMessageHandler(SendResponse("700123"));
        var router = Router();

        Assert.StartsWith("SIM-", (await router.SendAsync(Message)).ProviderReference, StringComparison.Ordinal);

        await Runtime.SetAsync(SettingKeys.SmsMode, "Wsdl");
        await ConfigureConnectionAsync();
        var paid = await router.SendAsync(Message);

        Assert.Equal("700123", paid.ProviderReference);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Router_ReportsSyntheticCreditInEveryModeExceptWsdl()
    {
        handler = new StubHttpMessageHandler(SendResponse("12500"));
        var router = Router();

        Assert.Equal(SimulatedSmsGateway.SimulatedCreditRial, (await router.GetBalanceAsync()).RawCredit);

        await Runtime.SetAsync(SettingKeys.SmsMode, "Api");
        Assert.Equal(SimulatedSmsGateway.SimulatedCreditRial, (await router.GetBalanceAsync()).RawCredit);

        await Runtime.SetAsync(SettingKeys.SmsMode, "Wsdl");
        await ConfigureConnectionAsync();
        Assert.Equal("12500", (await router.GetBalanceAsync()).RawCredit);
    }

    [Fact]
    public async Task MaxAttempts_FromSettingsReachesEveryRowQueuedForAMission()
    {
        var mission = await ConfirmAsync();
        Db.SmsTemplates.Add(new SmsTemplate(SmsEventType.MissionApproved, "تأیید شد"));
        await Db.SaveChangesAsync();
        Settings.Values[SettingKeys.SmsMaxAttempts] = "2";
        handler = new StubHttpMessageHandler(SendResponse("700123"));

        await Dispatcher().QueueForMissionAsync(mission.Id, SmsEventType.MissionApproved, null);

        var rows = await Db.SmsQueueItems.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count); // گیرنده پیش‌فرض: درخواست‌کننده و راننده.
        Assert.All(rows, row => Assert.Equal(2, row.MaxAttempts));
    }

    [Fact]
    public async Task MaxAttempts_StopsAutomaticRetriesAtTheConfiguredCeiling()
    {
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن آزمایشی",
            null, "tester", Clock.UtcNow, maxAttempts: 2);
        Db.SmsQueueItems.Add(item);
        await Db.SaveChangesAsync();
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        handler = new StubHttpMessageHandler(SendResponse("E6"));
        var dispatcher = Dispatcher();

        await dispatcher.ProcessQueueAsync(10);
        Assert.Equal(SmsQueueStatus.RetryScheduled, ReloadQueueItem(item.Id).Status);

        Clock.NowUtc = Clock.NowUtc.AddHours(2);
        await dispatcher.ProcessQueueAsync(10);
        Assert.Equal(SmsQueueStatus.Failed, ReloadQueueItem(item.Id).Status);
    }

    private SmsQueueItem ReloadQueueItem(Guid id) => Db.SmsQueueItems.AsNoTracking().First(item => item.Id == id);

    private SmsDispatcher Dispatcher() =>
        new(Db, Gateway(), Settings, Clock, NullLogger<SmsDispatcher>.Instance);

    private async Task<PayamSmsGateway> WiredGatewayAsync(
        string responseXml, HttpStatusCode status = HttpStatusCode.OK)
    {
        handler = new StubHttpMessageHandler(responseXml, status);
        var gateway = Gateway();
        await ConfigureConnectionAsync();
        return gateway;
    }

    private PayamSmsGateway Gateway()
    {
        factory = new StubHttpClientFactory(handler);
        return new PayamSmsGateway(factory, Runtime, NullLogger<PayamSmsGateway>.Instance);
    }

    private SmsGatewayRouter Router() =>
        new(Runtime, new SimulatedSmsGateway(), Gateway(), NullLogger<SmsGatewayRouter>.Instance);

    private async Task RouterCallsPayamAsync(string mode)
    {
        handler = new StubHttpMessageHandler(SendResponse("700123"));
        await Runtime.SetAsync(SettingKeys.SmsMode, mode);
        await ConfigureConnectionAsync();

        var result = await Router().SendAsync(Message);

        Assert.Equal("700123", result.ProviderReference);
        Assert.Equal(1, handler.CallCount);
    }

    private async Task ConfigureConnectionAsync()
    {
        await Runtime.SetAsync(SettingKeys.SmsEndpoint, Endpoint);
        await Runtime.SetAsync(SettingKeys.SmsOrganization, "ORG-1");
        await Runtime.SetAsync(SettingKeys.SmsUsername, "api-user");
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "api-pass");
        await Runtime.SetAsync(SettingKeys.SmsSenderNumber, "30001000");
    }

    private static string SendResponse(string id) =>
        "<tns:SendResponse xmlns:tns=\"urn:SMSAPIwsdl\"><SendReturn><SendResult><ID>" +
        WebUtility.HtmlEncode(id) + "</ID></SendResult></SendReturn></tns:SendResponse>";

    private static string SendStatus(string status) =>
        "<tns:getStatuses_OriginalIDResponse xmlns:tns=\"urn:SMSAPIwsdl\"><DeliveryResult><ID>700123</ID>" +
        "<Status>" + WebUtility.HtmlEncode(status) + "</Status></DeliveryResult></tns:getStatuses_OriginalIDResponse>";

    private static XNamespace Xns(string ns) => ns;

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

    private static string? Value(XElement parent, string localName) =>
        parent.Elements().Single(element => element.Name.LocalName == localName).Value;
}

/// <summary>یک فراخوان HTTP ثبت‌شده؛ بدنه هم به‌صورت متن و هم بایت خام نگه داشته می‌شود.</summary>
internal sealed record CapturedHttpRequest(string? Url, string? SoapAction, string? MediaType, string? Body, byte[] Raw);

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly string responseBody;
    private readonly HttpStatusCode statusCode;
    private readonly Exception? transportError;

    public StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        this.responseBody = responseBody;
        this.statusCode = statusCode;
    }

    public StubHttpMessageHandler(Exception transportError)
    {
        responseBody = string.Empty;
        statusCode = HttpStatusCode.OK;
        this.transportError = transportError;
    }

    public List<CapturedHttpRequest> Requests { get; } = [];

    public int CallCount => Requests.Count;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var raw = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var soapAction = request.Headers.TryGetValues("SOAPAction", out var values)
            ? string.Join(",", values)
            : null;

        Requests.Add(new CapturedHttpRequest(
            request.RequestUri?.ToString(),
            soapAction,
            request.Content?.Headers.ContentType?.MediaType,
            Encoding.UTF8.GetString(raw),
            raw));

        if (transportError is not null)
        {
            throw transportError;
        }

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "text/xml")
        };
    }
}

/// <summary>client ساختگی به‌ازای هر درخواست؛ تا timeout ست‌شده روی خود client قابل سنجش باشد.</summary>
internal sealed class StubHttpClientFactory(StubHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient? LastClient { get; private set; }

    public HttpClient CreateClient(string name)
    {
        LastClient = new HttpClient(handler, disposeHandler: false);
        return LastClient;
    }
}

using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// درگاه payamSMS نسخه ۲؛ SOAP 1.1 به سبک RPC/encoded مطابق بخش ۶ سند
/// docs/integrations/payam-sms-soap-v2.md (namespace `urn:SMSAPIwsdl`، ترتیب پارامترها از WSDL زنده).
/// همه تنظیمات از پایگاه داده خوانده می‌شوند تا تغییر درگاه نیاز به راه‌اندازی مجدد نداشته باشد.
/// </summary>
public sealed class PayamSmsGateway(
    IHttpClientFactory httpClientFactory,
    IRuntimeSettings settings,
    ILogger<PayamSmsGateway> logger) : ISmsGateway
{
    /// <summary>نام client ثبت‌شده در IHttpClientFactory.</summary>
    public const string HttpClientName = "payam-sms";

    internal const string ServiceNamespace = "urn:SMSAPIwsdl";
    internal const string SendAction = ServiceNamespace + "#Send";
    internal const string StatusAction = ServiceNamespace + "#getStatuses";
    internal const string BalanceAction = ServiceNamespace + "#return account balance";

    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace SoapEnc = "http://schemas.xmlsoap.org/soap/encoding/";
    private const string XsdPrefix = "xsd";
    private const string SoapEncPrefix = "SOAP-ENC";

    public async Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigAsync(cancellationToken);
        var missing = config.MissingParts();
        if (missing is not null)
        {
            return SmsSendResult.Transient(missing);
        }

        var destination = NormalizeDestination(message.Mobile);
        if (destination is null)
        {
            return SmsSendResult.Permanent($"شماره «{message.Mobile}» به قالب کد کشور (۹۸…) قابل تبدیل نیست.");
        }

        var envelope = BuildEnvelope("Send",
            Text("organization", config.Organization),
            Text("username", config.Username),
            Text("password", config.Password!),
            Text("srcNumber", config.SenderNumber),
            Text("body", message.Body),
            StringArray("destNo", [destination]));

        var (outcome, document, error) = await PostAsync(SendAction, envelope, config, cancellationToken);
        if (outcome == PostOutcome.TransportFailure)
        {
            return SmsSendResult.Transient(error!);
        }

        if (document is null)
        {
            return SmsSendResult.Transient(error ?? "پاسخ سرویس پیامک قابل‌خواندن نیست.");
        }

        var fault = PayamSmsFaults.Classify(ReadFault(document) ?? ReadFirstId(document));
        if (fault is not null)
        {
            return fault.Value.IsPermanent
                ? SmsSendResult.Permanent(fault.Value.Description)
                : SmsSendResult.Transient(fault.Value.Description);
        }

        // موفق: اولین SendResult با ID عددی؛ همان شناسه‌ای که بعداً با getStatuses_OriginalID رهگیری می‌شود.
        var reference = Results(document, "SendResult")
            .Select(item => Child(item, "ID"))
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && PayamSmsFaults.ParseCode(id) is null);

        return string.IsNullOrWhiteSpace(reference)
            ? SmsSendResult.Transient(error ?? "سرویس پیامک شناسه پیام برنگرداند.")
            : SmsSendResult.Success(reference.Trim(), null);
    }

    public async Task<SmsDeliveryResult> GetDeliveryStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(providerReference.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var messageId))
        {
            return new SmsDeliveryResult(SmsDeliveryState.Unknown, "شناسه پیام سرویس عددی نیست.");
        }

        var config = await LoadConfigAsync(cancellationToken);
        var missing = config.MissingParts();
        if (missing is not null)
        {
            return new SmsDeliveryResult(SmsDeliveryState.Unknown, missing);
        }

        var envelope = BuildEnvelope("getStatuses_OriginalID",
            Text("organization", config.Organization),
            Text("username", config.Username),
            Text("password", config.Password!),
            // نام پارامتر در WSDL با I بزرگ و جمع است: Ids
            LongArray("Ids", [messageId]));

        var (outcome, document, error) = await PostAsync(StatusAction, envelope, config, cancellationToken);
        if (outcome != PostOutcome.Ok || document is null)
        {
            return new SmsDeliveryResult(SmsDeliveryState.Unknown, error);
        }

        var raw = ReadFault(document)
            ?? Results(document, "DeliveryResult").Select(item => Child(item, "Status")).FirstOrDefault(value => value is not null);

        return MapDelivery(raw ?? error);
    }

    public async Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigAsync(cancellationToken);
        var missing = config.MissingParts();
        if (missing is not null)
        {
            return SmsBalanceResult.Failure(missing);
        }

        var envelope = BuildEnvelope("Balance",
            Text("organization", config.Organization),
            Text("username", config.Username),
            Text("password", config.Password!));

        var (outcome, document, error) = await PostAsync(BalanceAction, envelope, config, cancellationToken);
        if (outcome != PostOutcome.Ok || document is null)
        {
            return SmsBalanceResult.Failure(error ?? "ارتباط با سرویس پیامک برقرار نشد.");
        }

        var value = ReadElement(document, "return") ?? ReadElement(document, "BalanceResult") ?? ReadFirstId(document);
        if (string.IsNullOrWhiteSpace(value))
        {
            return SmsBalanceResult.Failure(error ?? "سرویس پیامک اعتباری برنگرداند.");
        }

        var fault = PayamSmsFaults.Classify(value);
        return fault is null
            ? SmsBalanceResult.Of(value.Trim())
            : SmsBalanceResult.Failure(fault.Value.Description);
    }

    /// <summary>
    /// نگاشت محافظه‌کارانه وضعیت تحویل: خطای صریح زودتر از موفق بررسی می‌شود و هر مقدار
    /// ناآشنا نامشخص می‌ماند تا پیامی که سرویس‌دهنده درباره‌اش قطعی نگفته، تحویل‌شده ثبت نشود.
    /// </summary>
    public static SmsDeliveryResult MapDelivery(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return new SmsDeliveryResult(SmsDeliveryState.Unknown, rawStatus);
        }

        var normalized = rawStatus.Trim();
        if (normalized.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Undelivered", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Expired", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ناموفق", StringComparison.Ordinal)
            || normalized.Contains("عدم تحویل", StringComparison.Ordinal)
            || normalized.Contains("برگشت", StringComparison.Ordinal))
        {
            return new SmsDeliveryResult(SmsDeliveryState.Failed, normalized);
        }

        if (normalized.Equals("Delivered", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("تحویل داده", StringComparison.Ordinal))
        {
            return new SmsDeliveryResult(SmsDeliveryState.Delivered, normalized);
        }

        return new SmsDeliveryResult(SmsDeliveryState.Unknown, normalized);
    }

    /// <summary>
    /// شماره گیرنده به قالب سرویس‌دهنده: کد کشور + شماره، حداکثر ۱۲ رقم.
    /// `0912…` و `912…` و `0098…` و `+98…` همه به `98912…` می‌رسند؛ هر چیز دیگر نامعتبر است.
    /// </summary>
    public static string? NormalizeDestination(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0098", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.StartsWith("0", StringComparison.Ordinal) && digits.Length is 10 or 11)
        {
            digits = "98" + digits[1..];
        }
        else if (digits.Length == 10 && digits.StartsWith("9", StringComparison.Ordinal))
        {
            digits = "98" + digits;
        }

        return digits is { Length: >= 11 and <= 12 } && digits.StartsWith("98", StringComparison.Ordinal)
            ? digits
            : null;
    }

    private async Task<Config> LoadConfigAsync(CancellationToken cancellationToken)
    {
        var values = await settings.ReadAllAsync(cancellationToken);
        var timeout = ReadInt(values, SettingKeys.SmsTimeoutSeconds, SettingKeys.DefaultSmsTimeoutSeconds);
        return new Config(
            Read(values, SettingKeys.SmsEndpoint),
            Read(values, SettingKeys.SmsOrganization),
            Read(values, SettingKeys.SmsUsername),
            await settings.GetSecretAsync(SettingKeys.SmsPassword, cancellationToken),
            Read(values, SettingKeys.SmsSenderNumber),
            Math.Clamp(timeout, 1, 120));
    }

    private enum PostOutcome
    {
        Ok,
        TransportFailure,
    }

    private async Task<(PostOutcome Outcome, XDocument? Document, string? Error)> PostAsync(
        string action,
        string envelope,
        Config config,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

            using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "text/xml")
            };
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{action}\"");

            using var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = Parse(content);

            if (!response.IsSuccessStatusCode)
            {
                var fault = document is null ? null : ReadFault(document);
                return (PostOutcome.Ok, fault is null ? null : document,
                    PayamSmsFaults.Classify(fault ?? $"HTTP {(int)response.StatusCode}")?.Description
                    ?? $"پاسخ HTTP {(int)response.StatusCode} از سرویس پیامک.");
            }

            return document is null
                ? (PostOutcome.TransportFailure, null, "پاسخ سرویس پیامک قابل‌خواندن نیست.")
                : (PostOutcome.Ok, document, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "فراخوان {Action} سرویس پیامک ناموفق بود.", action);
            return (PostOutcome.TransportFailure, null, "ارتباط با سرویس پیامک برقرار نشد.");
        }
    }

    private static string BuildEnvelope(string operation, params XElement[] parameters)
    {
        // ریشه هر چهار پیشوند لازمِ سبک encoded را اعلام می‌کند تا QNameهای داخل مقدارهای
        // xsi:type (مانند SOAP-ENC:Array و xsd:string) در سند حل شوند؛ پارامترها عمداً بی‌پیشوند
        // یعنی بدون namespace می‌مانند، دقیقاً همان چیزی که سرور PHP برای RPC انتظار دارد.
        var envelope = new XElement(
            Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute(XNamespace.Xmlns + XsdPrefix, "http://www.w3.org/2001/XMLSchema"),
            new XAttribute(XNamespace.Xmlns + SoapEncPrefix, SoapEnc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tns", ServiceNamespace),
            new XAttribute(Soap + "encodingStyle", SoapEnc.NamespaceName),
            new XElement(Soap + "Body",
                new XElement(XNamespace.Get(ServiceNamespace) + operation, parameters)));

        return envelope.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Text(string name, string? value) =>
        new(name, new XAttribute(Xsi + "type", $"{XsdPrefix}:string"), value ?? string.Empty);

    private static XElement StringArray(string name, IReadOnlyList<string> values) =>
        new(name,
            new XAttribute(Xsi + "type", $"{SoapEncPrefix}:Array"),
            new XAttribute(SoapEnc + "arrayType", $"{XsdPrefix}:string[{values.Count}]"),
            values.Select(value => new XElement("item", value)));

    private static XElement LongArray(string name, IReadOnlyList<long> values) =>
        new(name,
            new XAttribute(Xsi + "type", $"{SoapEncPrefix}:Array"),
            new XAttribute(SoapEnc + "arrayType", $"{XsdPrefix}:long[{values.Count}]"),
            values.Select(value => new XElement("item", value.ToString(CultureInfo.InvariantCulture))));

    private static XDocument? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(content, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static List<XElement> Results(XDocument document, string typeName)
    {
        var typed = document.Descendants()
            .Where(element => element.Name.LocalName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return typed.Count > 0
            ? typed
            : document.Descendants()
                .Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private static string? Child(XElement parent, string localName) =>
        parent.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

    private static string? ReadElement(XDocument document, string localName)
    {
        var value = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>سرویس‌دهنده خطا را یا در faultstring یا در فیلد ID تک‌سطری برمی‌گرداند.</summary>
    private static string? ReadFault(XDocument document) =>
        ReadElement(document, "faultstring") ?? ReadElement(document, "Fault");

    private static string? ReadFirstId(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName.Equals("ID", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

    private static string Read(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : string.Empty;

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        int.TryParse(values.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    internal sealed record Config(
        string Endpoint,
        string Organization,
        string Username,
        string? Password,
        string SenderNumber,
        int TimeoutSeconds)
    {
        /// <summary>پیام صریح برای مدیر، وقتی هنوز بخشی از اتصال پر نشده است.</summary>
        public string? MissingParts()
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                missing.Add("آدرس سرویس");
            }

            if (string.IsNullOrWhiteSpace(Username))
            {
                missing.Add("شناسه کاربری");
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                missing.Add("رمز");
            }

            if (string.IsNullOrWhiteSpace(SenderNumber))
            {
                missing.Add("شماره فرستنده");
            }

            return missing.Count == 0
                ? null
                : $"تنظیمات درگاه پیامک کامل نیست: {string.Join("، ", missing)}.";
        }
    }
}

/// <summary>
/// بیست‌ویک کد خطای سند section ۵. خطاهای «تلاش دوباره فایده ندارد» از خطاهای موقت جدا
/// می‌مانند تا صف، اعتبار یا شماره اشتباه را بی‌پایان تکرار نکند.
/// </summary>
public static class PayamSmsFaults
{
    private static readonly Dictionary<string, (string Description, bool IsPermanent)> Codes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["E1"] = ("احراز هویت HTTP در سرویس پیامک انجام نشده است.", true),
            ["E2"] = ("همه پارامترهای سرویس پیامک باید مقدار داشته باشند.", true),
            ["E3"] = ("نوع پارامترهای ارسالی به سرویس پیامک رشته نیست.", true),
            ["E4"] = ("دسترسی به سرویس پیامک مجاز نیست.", true),
            ["E5"] = ("شناسه کاربری یا رمز سرویس پیامک صحیح نیست.", true),
            ["E107"] = ("تعداد گیرندگان بیش از سقف ۲۵۰ گیرنده در هر فراخوانی است.", true),
            ["E223"] = ("شماره فرستنده اختصاصی در سرویس پیامک نامعتبر است.", true),
            ["E211"] = ("طول متن پیام بیش از حد مجاز است.", true),
            ["E6"] = ("اعتبار حساب پیامک برای ارسال کافی نیست.", false),
            ["E7"] = ("پیام ورودی تازه‌ای وجود ندارد.", false),
            ["E8"] = ("شناسه پیام برای دریافت وضعیت نامعتبر است.", true),
            ["E305"] = ("شناسه پیام برای دریافت وضعیت در سرویس پیامک پیدا نشد.", true),
            ["E65"] = ("شناسه پیام الزامی است.", true),
            ["E66"] = ("شماره فرستنده الزامی است.", true),
            ["E43"] = ("شماره گیرنده نامعتبر است.", true),
            ["E766"] = ("متن پیام خالی است.", true),
            ["E999"] = ("شماره اختصاصی منقضی شده است.", true),
            ["E998"] = ("بسته سرویس پیامک منقضی شده است.", true),
            ["E303"] = ("هیچ شماره گیرنده صحیحی ارسال نشده است.", true),
            ["E132"] = ("متن پیام شامل کلمات حساس است.", true),
            ["E800"] = ("گیرنده در لیست سیاه سرویس پیامک است.", true),
        };

    /// <summary>
    /// اگر متن پاسخ یک کد خطای سند باشد، توضیح فارسی و نوع خطا برگردانده می‌شود؛
    /// در غیر این صورت null یعنی پاسخ خطا نیست.
    /// </summary>
    public static (string Description, bool IsPermanent)? Classify(string? value)
    {
        var code = ParseCode(value);
        if (code is null)
        {
            return null;
        }

        return Codes.TryGetValue(code, out var known)
            ? ( $"{code} — {known.Description}", known.IsPermanent)
            : ($"{code} — خطای نامشخص سرویس پیامک.", false);
    }

    /// <summary>کد خطا یا در ابتدای متن است («E43 Recipient…») یا خودِ رشته‌ی کوتاه «E43».</summary>
    public static string? ParseCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var firstToken = value.Trim().Split([' ', '\t', '\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return firstToken.Length is >= 2 and <= 5 && firstToken[0] is 'e' or 'E'
            && firstToken[1..].All(char.IsDigit)
                ? firstToken
                : null;
    }
}

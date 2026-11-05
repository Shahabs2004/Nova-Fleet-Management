using System.Globalization;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaFleet.Application.Abstractions;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// درگاه SOAP سرویس ADP. ساختار پاکت بر پایه قرارداد عمومی SOAP 1.1 است و
/// نگاشت پاسخ تا دریافت نمونه واقعی سازمان «تأییدنشده» است؛ در نبود نگاشت قطعی، وضعیت «نامشخص» می‌ماند.
/// </summary>
public sealed class AdpSmsGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsOptions> options,
    ILogger<AdpSmsGateway> logger) : ISmsGateway
{
    private static readonly HashSet<string> PermanentErrorCodes =
        new(StringComparer.OrdinalIgnoreCase) { "E43", "E6", "E5", "E4", "E223", "E211", "E132", "E800" };

    private readonly SmsOptions _options = options.Value;

    public bool IsSimulation => false;

    public async Task<SmsSendResult> SendAsync(
        SmsOutboundMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            return SmsSendResult.Transient("تنظیمات درگاه پیامک کامل نیست.");
        }

        var body = BuildEnvelope(
            _options.SendAction,
            "SendSms",
            new Dictionary<string, string>
            {
                ["username"] = _options.Username!,
                ["password"] = _options.Password!,
                ["senderNumber"] = _options.SenderNumber ?? string.Empty,
                ["mobile"] = message.Mobile,
                ["message"] = message.Body,
            });

        try
        {
            var (statusCode, content) = await PostAsync(_options.SendAction, body, cancellationToken);
            var document = Parse(content);

            if (statusCode is < 200 or > 299)
            {
                var error = ReadError(document) ?? $"پاسخ HTTP {(int)statusCode}";
                return ClassifyError(error);
            }

            var reference = ReadElement(document, "MessageId")
                ?? ReadElement(document, "MessageID")
                ?? ReadElement(document, "Reference");
            var rawStatus = ReadElement(document, "Status") ?? ReadElement(document, "Result");

            if (!string.IsNullOrWhiteSpace(ReadElement(document, "ErrorCode")))
            {
                return ClassifyError(ReadError(document) ?? "خطای نامشخص سرویس‌دهنده");
            }

            return string.IsNullOrWhiteSpace(reference)
                ? SmsSendResult.Transient("سرویس‌دهنده شناسه پیام برنگرداند.", rawStatus)
                : SmsSendResult.Success(reference, rawStatus);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ارسال پیامک به ADP ناموفق بود؛ پیام در صف می‌ماند.");
            return SmsSendResult.Transient("ارتباط با سرویس پیامک برقرار نشد.");
        }
    }

    public async Task<SmsDeliveryResult> GetDeliveryStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            return new SmsDeliveryResult(SmsDeliveryState.Unknown, null);
        }

        var body = BuildEnvelope(
            _options.StatusAction,
            "GetDeliveryStatus",
            new Dictionary<string, string>
            {
                ["username"] = _options.Username!,
                ["password"] = _options.Password!,
                ["messageId"] = providerReference,
            });

        try
        {
            var (statusCode, content) = await PostAsync(_options.StatusAction, body, cancellationToken);
            if (statusCode is < 200 or > 299)
            {
                return new SmsDeliveryResult(SmsDeliveryState.Unknown, $"HTTP {(int)statusCode}");
            }

            var document = Parse(content);
            var raw = ReadElement(document, "Status") ?? ReadElement(document, "DeliveryStatus");
            return MapDelivery(raw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "استعلام وضعیت تحویل پیامک ناموفق بود.");
            return new SmsDeliveryResult(SmsDeliveryState.Unknown, null);
        }
    }

    /// <summary>
    /// نگاشت محافظه‌کارانه: فقط مقادیر صریح تحویل/ناموفق نگاشت می‌شوند و بقیه «نامشخص» می‌مانند
    /// تا وضعیت ناشناخته به‌اشتباه تحویل‌شده ثبت نشود.
    /// </summary>
    public static SmsDeliveryResult MapDelivery(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return new SmsDeliveryResult(SmsDeliveryState.Unknown, rawStatus);
        }

        var normalized = rawStatus.Trim();
        if (normalized.Equals("Delivered", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("7", StringComparison.Ordinal)
            || normalized.Contains("تحویل", StringComparison.Ordinal))
        {
            return new SmsDeliveryResult(SmsDeliveryState.Delivered, normalized);
        }

        if (normalized.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Undelivered", StringComparison.OrdinalIgnoreCase))
        {
            return new SmsDeliveryResult(SmsDeliveryState.Failed, normalized);
        }

        return new SmsDeliveryResult(SmsDeliveryState.Unknown, normalized);
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.Endpoint)
        && !string.IsNullOrWhiteSpace(_options.Username)
        && !string.IsNullOrWhiteSpace(_options.Password);

    private async Task<(int StatusCode, string Content)> PostAsync(
        string action,
        string envelope,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("adp-sms");
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "text/xml")
        };
        request.Headers.Add("SOAPAction", $"\"{action}\"");

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ((int)response.StatusCode, content);
    }

    private string BuildEnvelope(string action, string operation, IReadOnlyDictionary<string, string> fields)
    {
        var parameters = new StringBuilder();
        foreach (var (name, value) in fields)
        {
            parameters.Append('<').Append(name).Append('>')
                .Append(SecurityElement.Escape(value))
                .Append("</").Append(name).Append('>');
        }

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <{operation} xmlns="{_options.Endpoint}">
                  {parameters}
                </{operation}>
              </soap:Body>
            </soap:Envelope>
            """;
    }

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

    private static string? ReadElement(XDocument? document, string localName)
    {
        var value = document?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ReadError(XDocument? document) =>
        ReadElement(document, "ErrorDescription")
        ?? ReadElement(document, "Error")
        ?? ReadElement(document, "Message");

    private static SmsSendResult ClassifyError(string error)
    {
        var code = error.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return code is not null && PermanentErrorCodes.Contains(code)
            ? SmsSendResult.Permanent(error)
            : SmsSendResult.Transient(error);
    }
}

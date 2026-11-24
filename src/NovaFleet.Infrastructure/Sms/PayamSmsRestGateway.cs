using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;

namespace NovaFleet.Infrastructure.Sms;

/// <summary>
/// درگاه REST payamSMS (حالت Api). قرارداد از پروسیجر مرجع SP_SendSMS و فراخوان‌های فقط‌خواندنی
/// روی سرویس زنده استخراج شده است: docs/integrations/payam-sms-rest.md.
/// پاسخ پوششِ {code, message, data} است و کدهای خطای داخل data همان Eهای سند SOAP هستند.
/// همه تنظیمات از پایگاه داده خوانده می‌شوند تا تغییر درگاه نیاز به راه‌اندازی مجدد نداشته باشد.
/// </summary>
public sealed class PayamSmsRestGateway(
    IHttpClientFactory httpClientFactory,
    IRuntimeSettings settings,
    ILogger<PayamSmsRestGateway> logger) : ISmsGateway
{
    /// <summary>نام client ثبت‌شده در IHttpClientFactory.</summary>
    public const string HttpClientName = "payam-sms-rest";

    /// <summary>آدرس پیش‌فرض REST؛ وقتی تنظیم «آدرس سرویس» خالی باشد استفاده می‌شود.</summary>
    public const string DefaultEndpoint = "https://www.payamsms.com/services/rest/index.php";

    /// <summary>شناسه مشتری ثابت، مطابق پروسیجر مرجع SP_SendSMS.</summary>
    internal const int CustomerId = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // بدنه پیامک فارسی است؛ مثل پروسیجر مرجع به‌صورت UTF-8 خام ارسال می‌شود، نه فرارِ \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigAsync(cancellationToken);
        var missing = config.MissingParts();
        if (missing is not null)
        {
            return SmsSendResult.Transient(missing);
        }

        var destination = PayamSmsGateway.NormalizeDestination(message.Mobile);
        if (destination is null)
        {
            return SmsSendResult.Permanent($"شماره «{message.Mobile}» به قالب کد کشور (۹۸…) قابل تبدیل نیست.");
        }

        var payload = new
        {
            organization = config.Organization,
            username = config.Username,
            password = config.Password,
            method = "send",
            messages = new[]
            {
                new
                {
                    sender = config.SenderNumber,
                    recipient = destination,
                    body = message.Body,
                    customerId = CustomerId
                }
            }
        };

        var (envelope, error, indeterminate) = await PostAsync(payload, config, cancellationToken);
        if (envelope is null)
        {
            return indeterminate
                ? SmsSendResult.Indeterminate(error!)
                : SmsSendResult.Transient(error!);
        }

        if (envelope.Code != 200)
        {
            LogRejected(envelope);
            return envelope.Code == 111
                ? SmsSendResult.Permanent("درخواست ارسالی به سرویس پیامک معتبر نیست (کد ۱۱۱).")
                : SmsSendResult.Transient($"سرویس پیامک پاسخ ناموفق داد (کد {envelope.Code}).");
        }

        var result = MapSendData(envelope.Data);
        if (result.IsIndeterminate)
        {
            // بدنه خام می‌تواند شماره، متن پیام یا داده حساس داشته باشد؛ فقط شکل عمومی ثبت می‌شود.
            logger.LogWarning("پاسخ موفق درگاه REST به شناسه قابل استفاده پیام نگاشت نشد.");
        }

        return result;
    }

    public Task<SmsDeliveryResult> GetDeliveryStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
        // سرویس REST این سرویس‌دهنده متد وضعیت تحویل ندارد (بخش ۴ سند REST)؛ پاسخ صادقانه
        // «نامشخص» است تا پیام پذیرفته‌شده الکی تحویل‌شده یا ناموفق ثبت نشود.
        => Task.FromResult(new SmsDeliveryResult(
            SmsDeliveryState.Unknown,
            "درگاه REST این سرویس‌دهنده متد وضعیت تحویل ندارد."));

    public async Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigAsync(cancellationToken);
        var missing = config.MissingParts();
        if (missing is not null)
        {
            return SmsBalanceResult.Failure(missing);
        }

        var payload = new
        {
            organization = config.Organization,
            username = config.Username,
            password = config.Password,
            method = "getbalance"
        };

        var (envelope, error, _) = await PostAsync(payload, config, cancellationToken);
        if (envelope is null)
        {
            return SmsBalanceResult.Failure(error!);
        }

        if (envelope.Code != 200)
        {
            LogRejected(envelope);
            return envelope.Code == 111
                ? SmsBalanceResult.Failure("درخواست ارسالی به سرویس پیامک معتبر نیست (کد ۱۱۱).")
                : SmsBalanceResult.Failure($"سرویس پیامک پاسخ ناموفق داد (کد {envelope.Code}).");
        }

        var raw = envelope.Data.ValueKind switch
        {
            JsonValueKind.String => envelope.Data.GetString(),
            JsonValueKind.Number => envelope.Data.GetRawText(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return SmsBalanceResult.Failure("سرویس پیامک اعتباری برنگرداند.");
        }

        var fault = PayamSmsFaults.Classify(raw);
        return fault is null
            ? SmsBalanceResult.Of(raw.Trim())
            : SmsBalanceResult.Failure(fault.Value.Description);
    }

    /// <summary>
    /// شکل موفق پاسخ send در درگاه REST آرایه‌ای از {serverId, customerId, Mobile} است که
    /// serverId شناسه یکتای پیام (عدد) است؛ این شکل با پاسخ زنده تأیید شده است. برای سازگاری
    /// با قرارداد SOAP، ID نیز به‌عنوان جایگزین پذیرفته می‌شود. هم رشته و هم عدد به متن تبدیل
    /// می‌شوند؛ وگرنه پیامِ واقعاً ارسال‌شده «موقت» می‌ماند و صف آن را تکرار می‌کند.
    /// شکل خطای مشاهده‌شده: [{"ErrorCode":"E5"}].
    /// </summary>
    private static SmsSendResult MapSendData(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var mapped = MapSendItem(item);
                if (mapped is not null)
                {
                    return mapped;
                }
            }
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            var mapped = MapSendItem(data);
            if (mapped is not null)
            {
                return mapped;
            }
        }
        else if (data.ValueKind == JsonValueKind.String)
        {
            var fault = PayamSmsFaults.Classify(data.GetString());
            if (fault is not null)
            {
                return fault.Value.IsPermanent
                    ? SmsSendResult.Permanent(fault.Value.Description)
                    : SmsSendResult.Transient(fault.Value.Description);
            }
        }

        return SmsSendResult.Indeterminate("سرویس پیامک شناسه پیام برنگرداند.");
    }

    /// <summary>یک آیتم پاسخ send را به خطا یا شناسه پیام (serverId یا ID) نگاشت می‌کند؛ بدون شناسه و بدون خطا null است.</summary>
    private static SmsSendResult? MapSendItem(JsonElement item)
    {
        var reference = ReadReference(item, "serverId") ?? ReadReference(item, "ID");
        var fault = PayamSmsFaults.Classify(ReadString(item, "ErrorCode") ?? reference);
        if (fault is not null)
        {
            return fault.Value.IsPermanent
                ? SmsSendResult.Permanent(fault.Value.Description)
                : SmsSendResult.Transient(fault.Value.Description);
        }

        return string.IsNullOrWhiteSpace(reference)
            ? null
            : SmsSendResult.Success(reference.Trim(), null);
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>شناسه پیام ممکن است رشته یا عدد باشد؛ هر دو به متن تبدیل می‌شوند.</summary>
    private static string? ReadReference(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return null;
        }

        var reference = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(reference) ? null : reference;
    }

    private void LogRejected(RestEnvelope envelope) =>
        logger.LogWarning("پاسخ ناموفق درگاه REST پیامک: کد {Code}", envelope.Code);

    private async Task<(RestEnvelope? Envelope, string? Error, bool IsIndeterminate)> PostAsync(
        object payload, Config config, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(config.Endpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = ParseEnvelope(body);

            return envelope is null
                ? (null, response.IsSuccessStatusCode
                    ? "پاسخ سرویس پیامک قابل‌خواندن نیست."
                    : $"پاسخ HTTP {(int)response.StatusCode} از سرویس پیامک.", true)
                : (envelope, null, false);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("زمان انتظار فراخوان REST سرویس پیامک به پایان رسید.");
            return (null, "نتیجه ارسال به سرویس پیامک به‌دلیل پایان زمان انتظار نامشخص است.", true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "فراخوان REST سرویس پیامک با نتیجه نامشخص شکست خورد.");
            return (null, "ارتباط با سرویس پیامک برقرار نشد؛ نتیجه ارسال قابل تعیین نیست.", true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "فراخوان REST سرویس پیامک با پاسخ نامشخص شکست خورد.");
            return (null, "نتیجه ارسال به سرویس پیامک قابل تعیین نیست.", true);
        }
    }

    private static RestEnvelope? ParseEnvelope(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("code", out var code)
                || code.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var message = document.RootElement.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
            var data = document.RootElement.TryGetProperty("data", out var element)
                ? element.Clone()
                : default;

            return new RestEnvelope(code.GetInt32(), message, data);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Config> LoadConfigAsync(CancellationToken cancellationToken)
    {
        var values = await settings.ReadAllAsync(cancellationToken);
        var timeout = ReadInt(values, SettingKeys.SmsTimeoutSeconds, SettingKeys.DefaultSmsTimeoutSeconds);
        var endpoint = Read(values, SettingKeys.SmsEndpoint);
        return new Config(
            string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint,
            Read(values, SettingKeys.SmsOrganization),
            Read(values, SettingKeys.SmsUsername),
            await settings.GetSecretAsync(SettingKeys.SmsPassword, cancellationToken),
            Read(values, SettingKeys.SmsSenderNumber),
            Math.Clamp(timeout, 1, 120));
    }

    private static string Read(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : string.Empty;

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        int.TryParse(values.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private sealed record RestEnvelope(int Code, string? Message, JsonElement Data);

    private sealed record Config(
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
            if (string.IsNullOrWhiteSpace(Organization))
            {
                missing.Add("سازمان");
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
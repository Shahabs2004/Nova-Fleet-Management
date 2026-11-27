using System.Net;
using System.Net.Sockets;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Infrastructure.Security;

namespace NovaFleet.Infrastructure.Settings;

/// <summary>
/// تنها مجموعه قوانین اعتبارسنجی و نگاشت تنظیمات به کلیدهای جدول SystemSettings.
/// ویزارد راه‌اندازی و تب تنظیمات پس از نصب هر دو از همین متدها عبور می‌کنند، پس یک
/// مقدار نامعتبر در نصب همان خطایی را می‌دهد که پس از نصب می‌داد و برعکس.
/// متدهای Validate* هیچ چیزی نمی‌نویسند تا ویزارد بتواند پیش از تأیید نهایی همه بلوک‌ها
/// را با هم بسنجد و در صورت خطا حتی یک کلید هم ذخیره نشده باشد.
/// </summary>
public static class SettingsWriters
{
    /// <summary>فیلتر جست‌وجوی نرمال‌شده؛ برای ثبت در ممیزی.</summary>
    public static string ValidateDirectory(UpdateDirectorySettingsDto request)
    {
        if (request.Enabled)
        {
            Require(request.Host, "سرور دایرکتوری الزامی است.");
            Require(request.Domain, "نام دامنه الزامی است.");
            Require(request.BaseDn, "BaseDn الزامی است.");
        }

        if (request.Port is < 1 or > 65535)
        {
            throw new ValidationAppException("پورت دایرکتوری معتبر نیست.");
        }

        if (request.TimeoutSeconds is < 1 or > 120)
        {
            throw new ValidationAppException("تایم‌اوت دایرکتوری باید بین ۱ تا ۱۲۰ ثانیه باشد.");
        }

        var filter = request.SearchFilter?.Trim();
        if (string.IsNullOrWhiteSpace(filter) || !filter.Contains("{0}", StringComparison.Ordinal))
        {
            throw new ValidationAppException("فیلتر جست‌وجو باید شامل {0} باشد.");
        }

        return filter;
    }

    public static async Task<string> WriteDirectoryAsync(
        IRuntimeSettings settings,
        UpdateDirectorySettingsDto request,
        CancellationToken cancellationToken = default)
    {
        var filter = ValidateDirectory(request);

        await settings.SetAsync(SettingKeys.DirectoryEnabled, request.Enabled.ToString(), cancellationToken);
        await settings.SetAsync(SettingKeys.DirectoryHost, request.Host?.Trim() ?? string.Empty, cancellationToken);
        await settings.SetAsync(SettingKeys.DirectoryPort, request.Port.ToString(), cancellationToken);
        await settings.SetAsync(SettingKeys.DirectoryUseSsl, request.UseSsl.ToString(), cancellationToken);
        await settings.SetAsync(SettingKeys.DirectoryDomain, request.Domain?.Trim() ?? string.Empty, cancellationToken);
        await settings.SetAsync(SettingKeys.DirectoryBaseDn, request.BaseDn?.Trim() ?? string.Empty, cancellationToken);
        await settings.SetAsync(SettingKeys.DirectoryBindDn, request.BindDn?.Trim() ?? string.Empty, cancellationToken);
        await settings.SetAsync(SettingKeys.DirectoryTimeoutSeconds, request.TimeoutSeconds.ToString(), cancellationToken);
        await settings.SetAsync(SettingKeys.DirectorySearchFilter, filter, cancellationToken);
        await settings.SetSecretAsync(SettingKeys.DirectoryBindPassword, request.BindPassword, cancellationToken);
        return filter;
    }

    /// <summary>مقدار نرمال‌شده sms.mode؛ برای ثبت در ممیزی.</summary>
    public static string ValidateSms(UpdateSmsConnectionSettingsDto request) => ValidateSms(request, request.Password);

    private static string ValidateSms(UpdateSmsConnectionSettingsDto request, string? password)
    {
        var mode = Normalize(request.Mode);
        if (!SettingKeys.SmsModes.Contains(mode))
        {
            throw new ValidationAppException("روش ارسال پیامک باید Simulation، Wsdl یا Api باشد.");
        }

        if (request.TimeoutSeconds is < 1 or > 120)
        {
            throw new ValidationAppException("تایم‌اوت پیامک باید بین ۱ تا ۱۲۰ ثانیه باشد.");
        }

        if (request.RetryMinutes is < 1 or > 1440)
        {
            throw new ValidationAppException("فاصله تلاش مجدد پیامک باید بین ۱ تا ۱۴۴۰ دقیقه باشد.");
        }

        if (request.MaxAttempts is < 1 or > 20)
        {
            throw new ValidationAppException("حداکثر تعداد تلاش پیامک باید بین ۱ تا ۲۰ باشد.");
        }

        if (request.Enabled && !string.Equals(mode, "Simulation", StringComparison.OrdinalIgnoreCase))
        {
            Require(request.Endpoint, "آدرس سرویس پیامک الزامی است.");
            Require(request.Organization, "سازمان سرویس پیامک الزامی است.");
            Require(request.Username, "شناسه کاربری پیامک الزامی است.");
            Require(password, "رمز سرویس پیامک الزامی است.");
            Require(request.SenderNumber, "شماره فرستنده پیامک الزامی است.");
            ValidateEndpoint(request.Endpoint!);
        }

        return mode;
    }

    public static async Task<string> WriteSmsAsync(
        IRuntimeSettings settings,
        UpdateSmsConnectionSettingsDto request,
        CancellationToken cancellationToken = default)
    {
        var password = request.Password;
        if (password is null && request.Enabled
            && !string.Equals(Normalize(request.Mode), "Simulation", StringComparison.OrdinalIgnoreCase))
        {
            password = await settings.GetSecretAsync(SettingKeys.SmsPassword, cancellationToken);
        }

        var mode = ValidateSms(request, password);
        var writes = new List<SettingWrite>
        {
            new(SettingKeys.SmsMode, mode, false),
            new(SettingKeys.SmsEndpoint, request.Endpoint?.Trim() ?? string.Empty, false),
            new(SettingKeys.SmsOrganization, request.Organization?.Trim() ?? string.Empty, false),
            new(SettingKeys.SmsUsername, request.Username?.Trim() ?? string.Empty, false),
            new(SettingKeys.SmsSenderNumber, request.SenderNumber?.Trim() ?? string.Empty, false),
            new(SettingKeys.SmsTimeoutSeconds, request.TimeoutSeconds.ToString(), false),
            new(SettingKeys.SmsRetryMinutes, request.RetryMinutes.ToString(), false),
            new(SettingKeys.SmsMaxAttempts, request.MaxAttempts.ToString(), false),
        };

        if (request.Password is not null)
        {
            writes.Add(string.IsNullOrWhiteSpace(request.Password)
                ? new SettingWrite(SettingKeys.SmsPassword, string.Empty, false)
                : new SettingWrite(SettingKeys.SmsPassword, request.Password.Trim(), true));
        }

        // فعال‌سازی همیشه آخرین نوشتن است؛ اعتبارسنجی و محافظت رمز پیش از یک ذخیره مشترک انجام می‌شود.
        writes.Add(new SettingWrite(SettingKeys.SmsEnabled, request.Enabled.ToString(), false));
        await settings.SetBatchAsync(writes, cancellationToken);
        return mode;
    }

    public static OrganizationProfileDto ValidateOrganization(OrganizationProfileDto profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ValidationAppException("نام سازمان الزامی است.");
        }

        return new(profile.Name.Trim(), AvatarImageValidator.ValidateOrganizationLogo(profile.LogoDataUrl));
    }

    public static async Task<OrganizationProfileDto> WriteOrganizationAsync(
        IRuntimeSettings settings,
        OrganizationProfileDto profile,
        CancellationToken cancellationToken = default)
    {
        var normalized = ValidateOrganization(profile);

        await settings.SetAsync(SettingKeys.OrganizationName, normalized.Name, cancellationToken);
        await settings.SetAsync(SettingKeys.OrganizationLogo, normalized.LogoDataUrl ?? string.Empty, cancellationToken);
        return normalized;
    }

    public static void ValidateMissionRules(MissionRulesDto rules)
    {
        if (rules.MinimumGapMinutes is < 0 or > 240)
        {
            throw new ValidationAppException("فاصله اجباری باید بین ۰ تا ۲۴۰ دقیقه باشد.");
        }

        if (rules.FutureRequestDays is < 1 or > 365)
        {
            throw new ValidationAppException("حداکثر بازه آینده درخواست باید بین ۱ تا ۳۶۵ روز باشد.");
        }

        if (rules.BackdateRequestDays is < 1)
        {
            throw new ValidationAppException("بازه ثبت در گذشته باید نامحدود یا تعداد روز مثبت باشد.");
        }

        if (rules.WithdrawalAutoCloseHours is < 1 or > 168)
        {
            throw new ValidationAppException("مهلت بسته‌شدن خودکار انصراف باید بین ۱ تا ۱۶۸ ساعت باشد.");
        }

        if (rules.StartReminderMinutes is < 0 or > 1440)
        {
            throw new ValidationAppException("زمان یادآوری پیامک باید بین ۰ تا ۱۴۴۰ دقیقه باشد.");
        }
    }

    public static async Task WriteMissionRulesAsync(
        IRuntimeSettings settings,
        MissionRulesDto rules,
        CancellationToken cancellationToken = default)
    {
        ValidateMissionRules(rules);

        await settings.SetAsync(SettingKeys.MinimumGapMinutes, rules.MinimumGapMinutes.ToString(), cancellationToken);
        await settings.SetAsync(SettingKeys.FutureRequestDays, rules.FutureRequestDays.ToString(), cancellationToken);
        await settings.SetAsync(SettingKeys.BackdateRequestDays, rules.BackdateRequestDays?.ToString() ?? string.Empty, cancellationToken);
        await settings.SetAsync(SettingKeys.WithdrawalAutoCloseHours, rules.WithdrawalAutoCloseHours.ToString(), cancellationToken);
        await settings.SetAsync(SettingKeys.SmsReminderMinutes, rules.StartReminderMinutes.ToString(), cancellationToken);
    }

    public static void ValidateSessionPolicy(SessionPolicyDto policy)
    {
        if (policy.AbsoluteMinutes is < 15 or > 4320 || policy.IdleMinutes is < 5 or > 1440)
        {
            throw new ValidationAppException("بازه عمر یا عدم فعالیت نشست معتبر نیست.");
        }
    }

    public static async Task WriteSessionPolicyAsync(
        IRuntimeSettings settings,
        SessionPolicyDto policy,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionPolicy(policy);

        await settings.SetAsync(SettingKeys.SessionAbsoluteMinutes, policy.AbsoluteMinutes.ToString(), cancellationToken);
        await settings.SetAsync(SettingKeys.SessionIdleMinutes, policy.IdleMinutes.ToString(), cancellationToken);
    }

    public static async Task WriteOrderTemplateAsync(
        IRuntimeSettings settings,
        MissionOrderTemplateDto template,
        CancellationToken cancellationToken = default)
    {
        await settings.SetAsync(SettingKeys.MissionOrderHeader, template.HeaderText ?? string.Empty, cancellationToken);
        await settings.SetAsync(SettingKeys.MissionOrderFooter, template.FooterText ?? string.Empty, cancellationToken);
        await settings.SetAsync(SettingKeys.MissionOrderShowLogo, template.ShowLogo.ToString(), cancellationToken);
    }

    private static void Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationAppException(message);
        }
    }

    private static void ValidateEndpoint(string endpoint)
    {
        if (!IsValidEndpoint(endpoint))
        {
            throw new ValidationAppException(
                "آدرس سرویس پیامک باید HTTP/HTTPS معتبر و بدون اطلاعات ورود، قطعه URI، loopback یا نشانی link-local باشد.");
        }
    }

    /// <summary>قوانین مشترک ذخیره و اجرای درگاه؛ آدرس‌های خصوصیِ درگاه داخلی مجاز می‌مانند.</summary>
    public static bool IsValidEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.IsLoopback)
        {
            return false;
        }

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IPAddress.TryParse(uri.DnsSafeHost, out var address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        return !IPAddress.Any.Equals(address)
            && !IPAddress.IPv6Any.Equals(address)
            && !address.IsIPv6LinkLocal
            && !address.IsIPv6SiteLocal
            && !address.IsIPv6Multicast
            && !(address.AddressFamily == AddressFamily.InterNetwork
                && ((bytes[0] >= 224 && bytes[0] <= 239) || (bytes[0] == 169 && bytes[1] == 254)));
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}

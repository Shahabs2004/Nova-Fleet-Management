using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Logging;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;

namespace NovaFleet.Infrastructure.Directory;

/// <summary>
/// احراز هویت Active Directory از طریق LDAPS با اعتبارسنجی کامل گواهی.
/// تنظیمات از پایگاه داده خوانده می‌شود تا تغییرش بدون restart در نشست بعدی اعمال شود؛
/// هنگام خطا، پیام یکسان و غیرقابل‌شمارش برگردانده می‌شود و هیچ اطلاعات حساسی لاگ نمی‌شود.
/// </summary>
public sealed class LdapDirectoryAuthenticator(
    IRuntimeSettings settings,
    ILogger<LdapDirectoryAuthenticator> logger) : IDirectoryAuthenticator
{
    /// <summary>
    /// سقف نتایج آزمون اتصال. رسیدن به این عدد یعنی «حداقل این‌قدر کاربر هست» و
    /// تعداد کل دایرکتوری گزارش نمی‌شود.
    /// </summary>
    private const int ProbeSearchSizeLimit = 25;

    public async Task<DirectoryAuthResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var config = await ReadAsync(cancellationToken);
        if (!config.IsUsable)
        {
            return DirectoryAuthResult.Failed("ورود دامنه در این نصب فعال نیست.");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return DirectoryAuthResult.Failed("نام کاربری یا رمز دامنه معتبر نیست.");
        }

        return await Task.Run(() => AuthenticateCore(config, username.Trim(), password), cancellationToken);
    }

    public async Task<DirectoryUserInfo?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var config = await ReadAsync(cancellationToken);
        if (!config.IsUsable
            || string.IsNullOrWhiteSpace(config.BindDn)
            || string.IsNullOrEmpty(config.BindPassword)
            || string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return await Task.Run(() => FindCore(config, username.Trim()), cancellationToken);
    }

    public async Task<DirectoryProbeResult> ProbeAsync(
        string? sampleUsername,
        CancellationToken cancellationToken = default)
    {
        var config = await ReadAsync(cancellationToken);
        if (!config.IsUsable)
        {
            return DirectoryProbeResult.Failed("ورود دامنه در این نصب فعال نیست.");
        }

        if (string.IsNullOrWhiteSpace(config.BindDn) || string.IsNullOrEmpty(config.BindPassword))
        {
            return DirectoryProbeResult.Failed("حساب Bind یا رمز آن تنظیم نشده است.");
        }

        return await Task.Run(() => ProbeCore(config, sampleUsername?.Trim()), cancellationToken);
    }

    private async Task<DirectoryConfig> ReadAsync(CancellationToken cancellationToken)
    {
        var values = await settings.ReadAllAsync(cancellationToken);
        string Text(string key, string fallback = "") =>
            values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

        return new DirectoryConfig(
            ReadBool(values, SettingKeys.DirectoryEnabled),
            Text(SettingKeys.DirectoryHost),
            int.TryParse(Text(SettingKeys.DirectoryPort), out var port) ? port : SettingKeys.DefaultDirectoryPort,
            ReadBool(values, SettingKeys.DirectoryUseSsl),
            Text(SettingKeys.DirectoryDomain),
            Text(SettingKeys.DirectoryBaseDn),
            Text(SettingKeys.DirectorySearchFilter, SettingKeys.DefaultDirectorySearchFilter),
            Text(SettingKeys.DirectoryBindDn),
            await settings.GetSecretAsync(SettingKeys.DirectoryBindPassword, cancellationToken),
            int.TryParse(Text(SettingKeys.DirectoryTimeoutSeconds), out var timeout)
                ? timeout
                : SettingKeys.DefaultDirectoryTimeoutSeconds);
    }

    private DirectoryAuthResult AuthenticateCore(DirectoryConfig config, string username, string password)
    {
        try
        {
            using var connection = CreateConnection(config);
            connection.Credential = new NetworkCredential(BuildUpn(config, username), password);
            connection.Bind();
            var info = SearchUser(connection, config, username);
            return info is null
                ? DirectoryAuthResult.Failed("حساب کاربری در دایرکتوری پیدا نشد.")
                : DirectoryAuthResult.Succeeded(info);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            return DirectoryAuthResult.Failed("نام کاربری یا رمز دامنه معتبر نیست.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "اتصال LDAPS برای احراز هویت ناموفق بود.");
            return DirectoryAuthResult.Failed("ارتباط با سرویس دایرکتوری برقرار نشد.");
        }
    }

    private DirectoryUserInfo? FindCore(DirectoryConfig config, string username)
    {
        try
        {
            using var connection = CreateConnection(config);
            connection.Credential = new NetworkCredential(config.BindDn, config.BindPassword);
            connection.Bind();
            return SearchUser(connection, config, username);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "جست‌وجوی کاربر در LDAPS ناموفق بود.");
            return null;
        }
    }

    /// <summary>
    /// آزمون مدیر سامانه: bind با حساب Bind سپس یک جست‌وجوی نمونه با سقف اندازه.
    /// بدون نام کاربری، فیلتر با * اجرا می‌شود تا صرفاً حق خواندن سنجیده شود.
    /// </summary>
    private DirectoryProbeResult ProbeCore(DirectoryConfig config, string? sampleUsername)
    {
        try
        {
            using var connection = CreateConnection(config);
            connection.Credential = new NetworkCredential(config.BindDn, config.BindPassword);
            connection.Bind();

            var value = string.IsNullOrWhiteSpace(sampleUsername) ? "*" : EscapeFilter(sampleUsername);
            var request = new SearchRequest(
                config.BaseDn,
                config.SearchFilter.Replace("{0}", value, StringComparison.Ordinal),
                SearchScope.Subtree,
                "sAMAccountName")
            {
                SizeLimit = ProbeSearchSizeLimit
            };

            var found = connection.SendRequest(request) is SearchResponse response ? response.Entries.Count : 0;
            return DirectoryProbeResult.Bound(found, found >= ProbeSearchSizeLimit);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            return DirectoryProbeResult.Failed("حساب Bind یا رمز آن در دامنه پذیرفته نشد.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "آزمون اتصال LDAPS ناموفق بود.");
            return DirectoryProbeResult.Failed("ارتباط با سرویس دایرکتوری برقرار نشد.");
        }
    }

    private static LdapConnection CreateConnection(DirectoryConfig config)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(config.Host, config.Port))
        {
            AuthType = AuthType.Basic
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = config.UseSsl;
        connection.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        return connection;
    }

    private static DirectoryUserInfo? SearchUser(LdapConnection connection, DirectoryConfig config, string username)
    {
        var filter = config.SearchFilter.Replace("{0}", EscapeFilter(username), StringComparison.Ordinal);
        var request = new SearchRequest(
            config.BaseDn,
            filter,
            SearchScope.Subtree,
            "sAMAccountName",
            "displayName",
            "cn",
            "mail",
            "mobile",
            "memberOf",
            "distinguishedName");

        if (connection.SendRequest(request) is not SearchResponse response || response.Entries.Count == 0)
        {
            return null;
        }

        var entry = response.Entries[0];
        var accountName = Read(entry, "sAMAccountName") ?? username;
        var displayName = Read(entry, "displayName") ?? Read(entry, "cn") ?? accountName;
        var groups = entry.Attributes["memberOf"] is { } groupAttribute
            ? groupAttribute.GetValues(typeof(string)).Cast<string>().ToList()
            : [];

        return new DirectoryUserInfo(
            accountName,
            displayName,
            Read(entry, "mail"),
            Read(entry, "mobile"),
            groups,
            Read(entry, "distinguishedName"));
    }

    private static string BuildUpn(DirectoryConfig config, string username) =>
        username.Contains('@') ? username : $"{username}@{config.Domain}";

    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key) =>
        bool.TryParse(values.GetValueOrDefault(key), out var value) && value;

    private static string? Read(SearchResultEntry entry, string attribute) =>
        entry.Attributes[attribute] is { Count: > 0 } value
            ? value.GetValues(typeof(string)).Cast<string>().FirstOrDefault()
            : null;

    /// <summary>فرار دادن نویسه‌های ویژه فیلتر LDAP برای جلوگیری از تزریق فیلتر.</summary>
    private static string EscapeFilter(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '\\' => "\\5c",
                '*' => "\\2a",
                '(' => "\\28",
                ')' => "\\29",
                '\0' => "\\00",
                _ => character.ToString()
            });
        }

        return builder.ToString();
    }

    private sealed record DirectoryConfig(
        bool Enabled,
        string Host,
        int Port,
        bool UseSsl,
        string Domain,
        string BaseDn,
        string SearchFilter,
        string BindDn,
        string? BindPassword,
        int TimeoutSeconds)
    {
        public bool IsUsable => Enabled
            && !string.IsNullOrWhiteSpace(Host)
            && !string.IsNullOrWhiteSpace(Domain)
            && !string.IsNullOrWhiteSpace(BaseDn);
    }
}

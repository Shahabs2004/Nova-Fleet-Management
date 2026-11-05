using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaFleet.Application.Abstractions;

namespace NovaFleet.Infrastructure.Directory;

/// <summary>تنظیمات اتصال LDAPS؛ مقادیر واقعی فقط در محیط امن سازمان وارد می‌شوند.</summary>
public sealed class DirectoryOptions
{
    public const string SectionName = "Directory";

    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 636;

    public bool UseSsl { get; set; } = true;

    /// <summary>دامنه NetBIOS یا DNS برای ساخت UPN هنگام احراز هویت.</summary>
    public string Domain { get; set; } = string.Empty;

    public string BaseDn { get; set; } = string.Empty;

    /// <summary>حساب سرویس اختیاری برای جست‌وجوی کاربران در ورود ویندوز؛ رمز آن در تنظیمات محافظت‌شده است.</summary>
    public string? BindDn { get; set; }

    public string? BindPassword { get; set; }
}

/// <summary>
/// احراز هویت Active Directory از طریق LDAPS با اعتبارسنجی کامل گواهی.
/// هنگام خطا، پیام یکسان و غیرقابل‌شمارش برگردانده می‌شود و هیچ اطلاعات حساسی لاگ نمی‌شود.
/// </summary>
public sealed class LdapDirectoryAuthenticator(
    IOptions<DirectoryOptions> options,
    ILogger<LdapDirectoryAuthenticator> logger) : IDirectoryAuthenticator
{
    private readonly DirectoryOptions _options = options.Value;

    public bool IsConfigured =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.Host)
        && !string.IsNullOrWhiteSpace(_options.Domain)
        && !string.IsNullOrWhiteSpace(_options.BaseDn);

    public Task<DirectoryAuthResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(DirectoryAuthResult.Failed("ورود دامنه در این نصب فعال نیست."));
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return Task.FromResult(DirectoryAuthResult.Failed("نام کاربری یا رمز دامنه معتبر نیست."));
        }

        return Task.Run(() => AuthenticateCore(username.Trim(), password), cancellationToken);
    }

    public Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(_options.BindDn) || string.IsNullOrEmpty(_options.BindPassword))
        {
            return Task.FromResult<DirectoryUserInfo?>(null);
        }

        return Task.Run(() => FindCore(username.Trim()), cancellationToken);
    }

    private DirectoryAuthResult AuthenticateCore(string username, string password)
    {
        try
        {
            using var connection = CreateConnection();
            connection.Credential = new NetworkCredential(BuildUpn(username), password);
            connection.Bind();
            var info = SearchUser(connection, username);
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

    private DirectoryUserInfo? FindCore(string username)
    {
        try
        {
            using var connection = CreateConnection();
            connection.Credential = new NetworkCredential(_options.BindDn, _options.BindPassword);
            connection.Bind();
            return SearchUser(connection, username);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "جست‌وجوی کاربر در LDAPS ناموفق بود.");
            return null;
        }
    }

    private LdapConnection CreateConnection()
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(_options.Host, _options.Port))
        {
            AuthType = AuthType.Basic
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = _options.UseSsl;
        connection.Timeout = TimeSpan.FromSeconds(15);
        return connection;
    }

    private DirectoryUserInfo? SearchUser(LdapConnection connection, string username)
    {
        var filter = $"(&(objectClass=user)(sAMAccountName={EscapeFilter(username)}))";
        var request = new SearchRequest(
            _options.BaseDn,
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

    private string BuildUpn(string username) =>
        username.Contains('@') ? username : $"{username}@{_options.Domain}";

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
}

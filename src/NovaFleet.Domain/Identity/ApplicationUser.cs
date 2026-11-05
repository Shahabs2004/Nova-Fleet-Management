using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Identity;

/// <summary>
/// پروفایل کاربر سازمانی؛ می‌تواند حساب محلی یا همگام‌شده با دایرکتوری باشد.
/// غیرفعال‌سازی داخلی مستقل از وضعیت حساب در Active Directory است.
/// </summary>
public sealed class ApplicationUser : Entity
{
    private ApplicationUser()
    {
    }

    public string Username { get; private set; } = string.Empty;

    /// <summary>هش رمز برای حساب محلی؛ برای کاربر دایرکتوری خالی است.</summary>
    public string? PasswordHash { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string? MobilePhone { get; private set; }

    public string? Email { get; private set; }

    public string? OrganizationalUnit { get; private set; }

    public bool IsDisabled { get; private set; }

    /// <summary>مدیر محلی اضطراری؛ از چرخه عادی حذف/غیرفعال‌سازی خارج است.</summary>
    public bool IsLocalAdmin { get; private set; }

    public bool IsDirectoryUser { get; private set; }

    public string? DirectoryGroupsJson { get; private set; }

    public string? DirectoryDistinguishedName { get; private set; }

    public DateTimeOffset? LastDirectorySyncAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public string DisplayName => $"{FirstName} {LastName}".Trim() is { Length: > 0 } name ? name : Username;

    public ApplicationUser(string username, string firstName, string lastName, bool isLocalAdmin = false, bool isDirectoryUser = false)
    {
        Username = NormalizeUsername(username);
        FirstName = Required(firstName, 100, nameof(firstName));
        LastName = Required(lastName, 100, nameof(lastName));
        IsLocalAdmin = isLocalAdmin;
        IsDirectoryUser = isDirectoryUser;
    }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash, nameof(passwordHash));
        PasswordHash = passwordHash;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateProfile(string firstName, string lastName, string? mobilePhone, string? email, string? organizationalUnit)
    {
        FirstName = Required(firstName, 100, nameof(firstName));
        LastName = Required(lastName, 100, nameof(lastName));
        MobilePhone = Optional(mobilePhone, 30);
        Email = Optional(email, 200);
        OrganizationalUnit = Optional(organizationalUnit, 200);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SyncDirectory(string firstName, string lastName, string? organizationalUnit, string? groupsJson, string distinguishedName)
    {
        FirstName = string.IsNullOrWhiteSpace(firstName) ? FirstName : Required(firstName, 100, nameof(firstName));
        LastName = string.IsNullOrWhiteSpace(lastName) ? LastName : Required(lastName, 100, nameof(lastName));
        OrganizationalUnit = Optional(organizationalUnit, 200) ?? OrganizationalUnit;
        DirectoryGroupsJson = groupsJson;
        DirectoryDistinguishedName = Optional(distinguishedName, 1000);
        LastDirectorySyncAtUtc = DateTimeOffset.UtcNow;
        IsDirectoryUser = true;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Disable()
    {
        if (IsLocalAdmin)
        {
            throw new InvalidOperationException("حساب مدیر محلی اضطراری قابل غیرفعال‌سازی نیست.");
        }

        IsDisabled = true;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Enable()
    {
        IsDisabled = false;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkLocalAdmin(bool value)
    {
        IsLocalAdmin = value;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetDirectoryGroups(string? groupsJson)
    {
        DirectoryGroupsJson = groupsJson;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string NormalizeUsername(string username)
    {
        var normalized = Required(username, 64, nameof(username));
        return normalized.Contains('\\') ? normalized[(normalized.LastIndexOf('\\') + 1)..] : normalized;
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"حداکثر طول مجاز {maxLength} نویسه است.");
        }

        return normalized;
    }

    private static string? Optional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"حداکثر طول مجاز {maxLength} نویسه است.");
        }

        return normalized;
    }
}

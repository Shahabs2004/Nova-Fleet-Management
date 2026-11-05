namespace NovaFleet.Application.Abstractions;

/// <summary>اطلاعات کاربر دایرکتوری پس از احراز هویت یا جست‌وجو.</summary>
public sealed record DirectoryUserInfo(
    string Username,
    string DisplayName,
    string? Email,
    string? MobilePhone,
    IReadOnlyList<string> Groups,
    string? DistinguishedName);

public sealed record DirectoryAuthResult(bool Success, DirectoryUserInfo? User, string? Error)
{
    public static DirectoryAuthResult Failed(string error) => new(false, null, error);

    public static DirectoryAuthResult Succeeded(DirectoryUserInfo user) => new(true, user, null);
}

/// <summary>
/// درگاه احراز هویت Active Directory از طریق LDAPS.
/// در نبود تنظیمات معتبر، پیاده‌سازی باید صراحتاً شکست برگرداند و هرگز اعتبارسنجی گواهی را غیرفعال نکند.
/// </summary>
public interface IDirectoryAuthenticator
{
    bool IsConfigured { get; }

    Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
}

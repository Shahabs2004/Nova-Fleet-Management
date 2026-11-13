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
/// نتیجه آزمون اتصال دایرکتوری. شمارش از جست‌وجوی نمونه با سقف اندازه می‌آید، پس
/// HitSearchLimit یعنی «حداقل این‌قدر کاربر» و نه تعداد کل دایرکتوری.
/// </summary>
public sealed record DirectoryProbeResult(bool Success, string? Error, int FoundUsers, bool HitSearchLimit)
{
    public static DirectoryProbeResult Failed(string error) => new(false, error, 0, false);

    public static DirectoryProbeResult Bound(int foundUsers, bool hitSearchLimit) => new(true, null, foundUsers, hitSearchLimit);
}

/// <summary>
/// درگاه احراز هویت Active Directory از طریق LDAPS.
/// در نبود تنظیمات معتبر، پیاده‌سازی باید صراحتاً شکست برگرداند و هرگز اعتبارسنجی گواهی را غیرفعال نکند.
/// </summary>
public interface IDirectoryAuthenticator
{
    Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// آزمون فقط‌خواندنی برای مدیر سامانه: bind با حساب Bind و یک جست‌وجوی نمونه.
    /// نام کاربری اختیاری است؛ اگر داده شود همان فیلتر پیکربندی‌شده روی آن اجرا می‌شود.
    /// </summary>
    Task<DirectoryProbeResult> ProbeAsync(string? sampleUsername, CancellationToken cancellationToken = default);
}

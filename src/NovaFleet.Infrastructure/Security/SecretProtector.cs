using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace NovaFleet.Infrastructure.Security;

/// <summary>
/// رمزنگاری مقادیر سرّی تنظیمات با حلقه کلید Data Protection خود سامانه.
/// خروجی متن base64 است تا در همان ستون کلید/مقدار جا شود و هرگز به UI برنگردد.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>در نبود متن رمزنگاری‌شده معتبر (حذف پوشه کلیدها یا انتقال نصب) null برمی‌گرداند نه استثنا.</summary>
    string? Unprotect(string? protectedValue);
}

public sealed class DataProtectionSecretProtector(
    IDataProtectionProvider provider,
    ILogger<DataProtectionSecretProtector> logger) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("NovaFleet.Settings.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "رمزگشایی یک تنظیم سرّی ناموفق بود؛ حلقه کلیدها بررسی شود.");
            return null;
        }
    }
}

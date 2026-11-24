namespace NovaFleet.Application.Abstractions;

public sealed record SettingWrite(string Key, string? Value, bool IsSecret);

/// <summary>
/// تنظیمات runtime که در پایگاه داده نگهداری می‌شوند؛ برخلاف تنظیمات فایل، تغییر آن‌ها
/// از نشست بعدی اعمال می‌شود و فقط موارد خوانده‌شده در زمان راه‌اندازی به restart نیاز دارند.
/// مقادیر اسرار فقط با GetSecretAsync و رمزگشایی‌شده در اختیار سرویس‌ها قرار می‌گیرد.
/// </summary>
public interface IRuntimeSettings
{
    /// <summary>همه کلیدهای غیرسرّی؛ برای خواندن دسته‌ای در یک پرس‌وجو.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default);

    Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default);

    /// <summary>رمزگشایی مقدار سرّی؛ null یعنی تنظیم نشده یا کلید رمزنگاری در دسترس نیست.</summary>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>
    /// همه نوشتن‌ها را با یک SaveChanges ذخیره می‌کند. Value سرّیِ null یعنی «تغییر نده»؛
    /// Value غیرسرّیِ null مانند رفتار SetAsync رشته خالی ذخیره می‌شود.
    /// </summary>
    Task SetBatchAsync(IReadOnlyList<SettingWrite> writes, CancellationToken cancellationToken = default);

    /// <summary>plaintext خالی یعنی «تغییر نده»؛ برای پاک کردن از ClearSecretAsync استفاده کنید.</summary>
    Task SetSecretAsync(string key, string? plaintext, CancellationToken cancellationToken = default);

    Task ClearSecretAsync(string key, CancellationToken cancellationToken = default);
}

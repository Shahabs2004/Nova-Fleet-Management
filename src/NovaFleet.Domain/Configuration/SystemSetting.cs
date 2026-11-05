using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Configuration;

/// <summary>
/// تنظیم عملیاتی سامانه به‌صورت کلید/مقدار. مقدار رمزآلود در این جدول نگهداری نمی‌شود؛
/// اطلاعات حساس اتصال فقط در تنظیمات محافظت‌شده محیط قرار می‌گیرد.
/// </summary>
public sealed class SystemSetting : Entity
{
    private SystemSetting()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public SystemSetting(string key, string value, string? description = null)
    {
        Key = Required(key, 200, nameof(key));
        Value = value ?? string.Empty;
        Description = description?.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetValue(string value, DateTimeOffset nowUtc)
    {
        Value = value ?? string.Empty;
        UpdatedAtUtc = nowUtc;
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return normalized;
    }
}

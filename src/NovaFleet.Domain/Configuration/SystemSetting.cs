using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Configuration;

/// <summary>
/// تنظیم عملیاتی سامانه به‌صورت کلید/مقدار. کلیدهای اعلام‌شده در SettingKeys.Secrets
/// مقدارشان را رمزنگاری‌شده در ستون ProtectedValue نگه می‌دارند و ستون Value برای آن‌ها خالی می‌ماند.
/// </summary>
public sealed class SystemSetting : Entity
{
    private SystemSetting()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    /// <summary>بله بودن یعنی مقدار واقعی فقط در ProtectedValue و به‌صورت رمزنگاری‌شده است.</summary>
    public bool IsSecret { get; private set; }

    public string? ProtectedValue { get; private set; }

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

    public bool HasProtectedValue => !string.IsNullOrWhiteSpace(ProtectedValue);

    public void SetValue(string value, DateTimeOffset nowUtc)
    {
        Value = value ?? string.Empty;
        IsSecret = false;
        ProtectedValue = null;
        UpdatedAtUtc = nowUtc;
    }

    public void SetProtectedValue(string protectedValue, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            throw new ArgumentException("مقدار رمزنگاری‌شده نمی‌تواند خالی باشد.", nameof(protectedValue));
        }

        Value = string.Empty;
        IsSecret = true;
        ProtectedValue = protectedValue;
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

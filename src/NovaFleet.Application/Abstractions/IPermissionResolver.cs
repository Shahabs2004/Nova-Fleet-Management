using NovaFleet.Application.Authorization;

namespace NovaFleet.Application.Abstractions;

/// <summary>عکس فوری دسترسی کاربر برای ساخت هویت درخواست و نمایش در رابط کاربری.</summary>
public sealed record UserAccessSnapshot(
    string Username,
    string DisplayName,
    string? OrganizationalUnit,
    bool IsLocalAdmin,
    bool IsDisabled,
    IReadOnlyList<string> Roles,
    EffectivePermissionSet Permissions)
{
    /// <summary>فهرست مجوزهای مؤثر و مشخص برای رابط کاربری (بدون wildcard).</summary>
    public IReadOnlyList<string> EffectiveCodes =>
        PermissionCatalog.Definitions
            .Where(definition => !definition.Code.Contains('*'))
            .Where(definition => Permissions.Has(definition.Code))
            .Select(definition => definition.Code)
            .ToList();
}

/// <summary>محاسبه مجوزهای مؤثر از نقش‌ها، مجوزهای مستقیم و نگاشت گروه‌های دایرکتوری.</summary>
public interface IPermissionResolver
{
    Task<UserAccessSnapshot?> ResolveAsync(string username, CancellationToken cancellationToken = default);

    Task<UserAccessSnapshot?> ResolveAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>کاربران فعالی که مجوز مشخص را دارند؛ برای اعلان‌های گروهی.</summary>
    Task<IReadOnlyList<string>> ListUsernamesWithPermissionAsync(string permission, CancellationToken cancellationToken = default);
}

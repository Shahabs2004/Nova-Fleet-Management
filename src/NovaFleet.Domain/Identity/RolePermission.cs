namespace NovaFleet.Domain.Identity;

/// <summary>مجوز تخصیص‌یافته به نقش با اثر اعطا یا منع.</summary>
public sealed class RolePermission
{
    private RolePermission()
    {
    }

    public Guid RoleId { get; private set; }

    public string Permission { get; private set; } = string.Empty;

    public PermissionEffect Effect { get; private set; } = PermissionEffect.Grant;

    public RolePermission(Guid roleId, string permission, PermissionEffect effect)
    {
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("شناسه نقش معتبر نیست.", nameof(roleId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(permission, nameof(permission));
        RoleId = roleId;
        Permission = permission.Trim();
        Effect = effect;
    }
}

namespace NovaFleet.Domain.Identity;

/// <summary>مجوز مستقیم کاربر که بر مجوزهای نقش‌ها اعمال می‌شود.</summary>
public sealed class UserPermission
{
    private UserPermission()
    {
    }

    public Guid UserId { get; private set; }

    public string Permission { get; private set; } = string.Empty;

    public PermissionEffect Effect { get; private set; } = PermissionEffect.Grant;

    public UserPermission(Guid userId, string permission, PermissionEffect effect)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("شناسه کاربر معتبر نیست.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(permission, nameof(permission));
        UserId = userId;
        Permission = permission.Trim();
        Effect = effect;
    }
}

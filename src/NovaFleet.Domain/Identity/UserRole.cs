namespace NovaFleet.Domain.Identity;

/// <summary>اتصال چندبه‌چند کاربر و نقش.</summary>
public sealed class UserRole
{
    private UserRole()
    {
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public UserRole(Guid userId, Guid roleId)
    {
        if (userId == Guid.Empty || roleId == Guid.Empty)
        {
            throw new ArgumentException("شناسه کاربر یا نقش معتبر نیست.");
        }

        UserId = userId;
        RoleId = roleId;
    }
}

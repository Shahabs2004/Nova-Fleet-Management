using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Identity;

/// <summary>نگاشت گروه Active Directory به نقش داخلی سامانه.</summary>
public sealed class DirectoryGroupRoleMapping : Entity
{
    private DirectoryGroupRoleMapping()
    {
    }

    public string GroupName { get; private set; } = string.Empty;

    public Guid RoleId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DirectoryGroupRoleMapping(string groupName, Guid roleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName, nameof(groupName));
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("شناسه نقش معتبر نیست.", nameof(roleId));
        }

        GroupName = groupName.Trim();
        RoleId = roleId;
    }

    public void Disable() => IsActive = false;

    public void Enable() => IsActive = true;
}

using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;

namespace NovaFleet.Api.Identity;

/// <summary>حامل هویت درخواست جاری؛ توسط میان‌افزار پس از احراز هویت پر می‌شود.</summary>
public sealed class CurrentUserHolder
{
    public ICurrentUser User { get; set; } = AnonymousCurrentUser.Instance;
}

/// <summary>هویت ساخته‌شده از عکس فوری دسترسی؛ مجوزها یک‌بار برای هر درخواست محاسبه می‌شوند.</summary>
public sealed class ResolvedCurrentUser(UserAccessSnapshot snapshot) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string Username => snapshot.Username;

    public string DisplayName => snapshot.DisplayName;

    public bool IsLocalAdmin => snapshot.IsLocalAdmin;

    public EffectivePermissionSet Permissions => snapshot.Permissions;

    public UserAccessSnapshot Snapshot => snapshot;
}

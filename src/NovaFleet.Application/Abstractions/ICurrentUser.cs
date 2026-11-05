using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;

namespace NovaFleet.Application.Abstractions;

/// <summary>هویت کاربر جاری درخواست؛ مجوزها یک‌بار از پایگاه داده محاسبه و در اینجا نگهداری می‌شوند.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string Username { get; }

    string DisplayName { get; }

    bool IsLocalAdmin { get; }

    EffectivePermissionSet Permissions { get; }

    bool Has(string permission) => Permissions.Has(permission);

    /// <summary>الزام مجوز در لایه سرویس؛ خطای نبود مجوز به پاسخ 403 نگاشت می‌شود.</summary>
    void Require(string permission)
    {
        if (!Has(permission))
        {
            throw new ForbiddenAppException("برای انجام این عملیات مجوز ندارید.");
        }
    }

    /// <summary>الزام داشتن دست‌کم یکی از مجوزهای خواسته‌شده.</summary>
    void RequireAny(params string[] permissions)
    {
        if (!permissions.Any(Has))
        {
            throw new ForbiddenAppException("برای انجام این عملیات مجوز ندارید.");
        }
    }
}

/// <summary>کاربر جاری وقتی درخواست معتبر و احراز هویت‌شده وجود ندارد.</summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public static AnonymousCurrentUser Instance { get; } = new();

    public bool IsAuthenticated => false;

    public string Username => string.Empty;

    public string DisplayName => string.Empty;

    public bool IsLocalAdmin => false;

    public EffectivePermissionSet Permissions => EffectivePermissionSet.Empty;
}

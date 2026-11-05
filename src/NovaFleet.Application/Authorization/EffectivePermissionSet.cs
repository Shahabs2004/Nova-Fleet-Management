using NovaFleet.Domain.Identity;

namespace NovaFleet.Application.Authorization;

/// <summary>
/// محاسبه مجوزهای مؤثر با قاعده «Deny بر Grant اولویت دارد».
/// ورودی، فهرست تخصیص‌های نقش‌ها و کاربر است و خروجی فقط برای همان درخواست معنا دارد.
/// </summary>
public sealed class EffectivePermissionSet
{
    private readonly HashSet<string> _grants;
    private readonly HashSet<string> _denials;

    private EffectivePermissionSet(HashSet<string> grants, HashSet<string> denials)
    {
        _grants = grants;
        _denials = denials;
    }

    public static EffectivePermissionSet Empty { get; } = new([], []);

    public static EffectivePermissionSet Build(
        IEnumerable<(string Permission, PermissionEffect Effect)> assignments)
    {
        var grants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var denials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (permission, effect) in assignments)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                continue;
            }

            var code = permission.Trim();
            if (effect == PermissionEffect.Deny)
            {
                denials.Add(code);
            }
            else
            {
                grants.Add(code);
            }
        }

        return new EffectivePermissionSet(grants, denials);
    }

    public bool Has(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        var code = permission.Trim();
        if (IsDenied(code))
        {
            return false;
        }

        return _grants.Contains(PermissionCatalog.All)
            || _grants.Contains(code)
            || _grants.Contains(ModuleWildcard(code));
    }

    /// <summary>آیا حداقل یکی از مجوزهای خواسته‌شده مؤثر است؟</summary>
    public bool HasAny(params string[] permissions) => permissions.Any(Has);

    public IReadOnlyCollection<string> Grants => _grants;

    private bool IsDenied(string code) =>
        _denials.Contains(PermissionCatalog.All)
        || _denials.Contains(code)
        || _denials.Contains(ModuleWildcard(code));

    private static string ModuleWildcard(string permission)
    {
        var dot = permission.IndexOf('.');
        return dot > 0 ? $"{permission[..dot]}.*" : permission;
    }
}

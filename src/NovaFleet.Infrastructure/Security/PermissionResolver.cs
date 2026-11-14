using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Security;

/// <summary>
/// محاسبه مجوزها از سه منبع: نقش‌های مستقیم، مجوزهای مستقیم کاربر و نقش‌های نگاشت‌شده از گروه‌های دایرکتوری.
/// نقش غیرفعال هیچ مجوزی نمی‌دهد و Deny همیشه بر Grant غالب است.
/// </summary>
public sealed class PermissionResolver(NovaFleetDbContext db) : IPermissionResolver
{
    public async Task<UserAccessSnapshot?> ResolveAsync(string username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var normalized = username.Trim();
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Username == normalized, cancellationToken);
        return user is null ? null : await BuildAsync(user, cancellationToken);
    }

    public async Task<UserAccessSnapshot?> ResolveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        return user is null ? null : await BuildAsync(user, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListUsernamesWithPermissionAsync(
        string permission,
        CancellationToken cancellationToken = default)
    {
        var users = await db.Users.AsNoTracking()
            .Where(item => !item.IsDisabled)
            .ToListAsync(cancellationToken);

        var result = new List<string>();
        foreach (var user in users)
        {
            var snapshot = await BuildAsync(user, cancellationToken);
            if (snapshot.Permissions.Has(permission))
            {
                result.Add(snapshot.Username);
            }
        }

        return result;
    }

    private async Task<UserAccessSnapshot> BuildAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var assignments = new List<(string Permission, PermissionEffect Effect)>();
        var roles = new List<string>();

        var directRoleIds = await db.UserRoles.AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .Select(item => item.RoleId)
            .ToListAsync(cancellationToken);

        var roleIds = new HashSet<Guid>(directRoleIds);

        var groups = ReadGroups(user.DirectoryGroupsJson);
        if (groups.Count > 0)
        {
            var mappings = await db.DirectoryGroupRoleMappings.AsNoTracking()
                .Where(item => item.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var mapping in mappings)
            {
                if (groups.Any(group => GroupMatches(group, mapping.GroupName)))
                {
                    roleIds.Add(mapping.RoleId);
                }
            }
        }

        if (roleIds.Count > 0)
        {
            var roleList = await db.Roles.AsNoTracking()
                .Where(item => roleIds.Contains(item.Id) && item.IsActive)
                .Select(item => new { item.Id, item.Name })
                .ToListAsync(cancellationToken);

            roles.AddRange(roleList.Select(item => item.Name));

            var activeRoleIds = roleList.Select(item => item.Id).ToList();
            var rolePermissions = await db.RolePermissions.AsNoTracking()
                .Where(item => activeRoleIds.Contains(item.RoleId))
                .Select(item => new { item.Permission, item.Effect })
                .ToListAsync(cancellationToken);

            assignments.AddRange(rolePermissions.Select(item => (item.Permission, item.Effect)));
        }

        var directPermissions = await db.UserPermissions.AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .Select(item => new { item.Permission, item.Effect })
            .ToListAsync(cancellationToken);

        assignments.AddRange(directPermissions.Select(item => (item.Permission, item.Effect)));

        if (user.IsLocalAdmin)
        {
            assignments.Add((PermissionCatalog.All, PermissionEffect.Grant));
            roles.Insert(0, "Administrator");
        }

        return new UserAccessSnapshot(
            user.Username,
            user.DisplayName,
            user.OrganizationalUnit,
            user.IsLocalAdmin,
            user.IsDisabled,
            roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            EffectivePermissionSet.Build(assignments),
            user.AvatarDataUrl);
    }

    private static List<string> ReadGroups(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>گروه دایرکتوری ممکن است DN کامل یا نام CN باشد؛ هر دو شکل با نگاشت مقایسه می‌شود.</summary>
    private static bool GroupMatches(string directoryGroup, string mappedName)
    {
        var mapped = mappedName.Trim();
        if (string.Equals(directoryGroup.Trim(), mapped, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var commonName = directoryGroup.Split(',', 2)[0];
        if (commonName.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            commonName = commonName[3..].Trim();
        }

        return string.Equals(commonName, mapped, StringComparison.OrdinalIgnoreCase);
    }
}

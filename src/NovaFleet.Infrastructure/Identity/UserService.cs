using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Identity;

/// <summary>مدیریت کاربران محلی/دایرکتوری، نقش‌ها و مجوزهای مستقیم.</summary>
public sealed class UserService(
    NovaFleetDbContext db,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    IAuditTrail audit) : IUserService
{
    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9._-]{3,64}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<UserListItemDto>> ListAsync(
        string? search,
        bool includeDisabled,
        CancellationToken cancellationToken = default)
    {
        var source = db.Users.AsNoTracking();
        if (!includeDisabled)
        {
            source = source.Where(item => !item.IsDisabled);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            source = source.Where(item =>
                item.Username.Contains(term)
                || item.FirstName.Contains(term)
                || item.LastName.Contains(term));
        }

        var users = await source.OrderBy(item => item.Username).Take(500).ToListAsync(cancellationToken);
        var userIds = users.Select(item => item.Id).ToList();
        var roles = await RoleNamesAsync(userIds, cancellationToken);

        return users.Select(user => new UserListItemDto(
            user.Id,
            user.Username,
            user.DisplayName,
            user.OrganizationalUnit,
            user.IsDisabled,
            user.IsDirectoryUser,
            user.IsLocalAdmin,
            roles.GetValueOrDefault(user.Id, []))).ToList();
    }

    public async Task<UserDetailsDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(id, cancellationToken);
        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(item => item.UserId == id)
            .Select(item => item.RoleId)
            .ToListAsync(cancellationToken);
        var roles = await RoleNamesAsync([id], cancellationToken);
        var permissions = await db.UserPermissions.AsNoTracking()
            .Where(item => item.UserId == id)
            .Select(item => new PermissionAssignmentDto(item.Permission, item.Effect))
            .ToListAsync(cancellationToken);

        return ToDetails(user, roleIds, roles.GetValueOrDefault(id, []), permissions);
    }

    public async Task<UserDetailsDto> CreateLocalAsync(
        CreateLocalUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (!UsernamePattern.IsMatch(username))
        {
            throw new ValidationAppException("نام کاربری باید ۳ تا ۶۴ نویسه و شامل حروف انگلیسی، رقم، نقطه، خط تیره یا زیرخط باشد.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            throw new ValidationAppException("رمز عبور باید حداقل ۸ نویسه باشد.");
        }

        if (await db.Users.AnyAsync(item => item.Username == username, cancellationToken))
        {
            throw new ConflictAppException("این نام کاربری قبلاً ثبت شده است.");
        }

        var user = new ApplicationUser(username, request.FirstName, request.LastName, request.IsLocalAdmin);
        user.SetPasswordHash(passwordHasher.Hash(request.Password));
        user.UpdateProfile(request.FirstName, request.LastName, request.MobilePhone, request.Email, request.OrganizationalUnit);
        db.Users.Add(user);
        await ReplaceRolesAsync(user.Id, request.RoleIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("CreateUser", "User", user.Id.ToString(), "Success", After: new { user.Username, user.IsLocalAdmin }), cancellationToken);

        return await GetAsync(user.Id, cancellationToken);
    }

    public async Task<UserDetailsDto> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(id, cancellationToken);
        user.UpdateProfile(request.FirstName, request.LastName, request.MobilePhone, request.Email, request.OrganizationalUnit);
        await ReplaceRolesAsync(id, request.RoleIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("UpdateUser", "User", id.ToString(), "Success"), cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<UserDetailsDto> SetDisabledAsync(Guid id, bool disabled, CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(id, cancellationToken);
        if (disabled && string.Equals(user.Username, currentUser.Username, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationAppException("کاربر نمی‌تواند حساب خود را غیرفعال کند.");
        }

        if (disabled)
        {
            user.Disable();
        }
        else
        {
            user.Enable();
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord(disabled ? "DisableUser" : "EnableUser", "User", id.ToString(), "Success"), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(id, cancellationToken);
        if (user.IsDirectoryUser)
        {
            throw new ValidationAppException("رمز کاربران دامنه در Active Directory مدیریت می‌شود.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            throw new ValidationAppException("رمز جدید باید حداقل ۸ نویسه باشد.");
        }

        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("ResetPassword", "User", id.ToString(), "Success"), cancellationToken);
    }

    public async Task SetPermissionsAsync(Guid id, SetPermissionsRequest request, CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(id, cancellationToken);
        var assignments = ValidateAssignments(request.Permissions, allowAll: false);

        var existing = await db.UserPermissions.Where(item => item.UserId == id).ToListAsync(cancellationToken);
        db.UserPermissions.RemoveRange(existing);
        db.UserPermissions.AddRange(assignments.Select(item => new UserPermission(id, item.Permission, item.Effect)));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("SetUserPermissions", "User", id.ToString(), "Success", After: assignments), cancellationToken);
    }

    private async Task<ApplicationUser> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
        ?? throw new NotFoundAppException("کاربر پیدا نشد.");

    private async Task ReplaceRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        var existing = await db.UserRoles.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        db.UserRoles.RemoveRange(existing);

        var distinct = roleIds.Where(item => item != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return;
        }

        var valid = await db.Roles.Where(item => distinct.Contains(item.Id)).Select(item => item.Id).ToListAsync(cancellationToken);
        db.UserRoles.AddRange(valid.Select(roleId => new UserRole(userId, roleId)));
    }

    private async Task<Dictionary<Guid, IReadOnlyList<string>>> RoleNamesAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var rows = await (
                from userRole in db.UserRoles.AsNoTracking()
                join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(item => item.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(item => item.Name).ToList());
    }

    private static List<(string Permission, PermissionEffect Effect)> ValidateAssignments(
        IReadOnlyList<PermissionAssignmentDto> permissions,
        bool allowAll)
    {
        var result = new List<(string, PermissionEffect)>();
        foreach (var assignment in permissions)
        {
            var permission = assignment.Permission?.Trim() ?? string.Empty;
            if (permission == PermissionCatalog.All)
            {
                if (!allowAll)
                {
                    throw new ValidationAppException("مجوز فراگیر فقط از طریق نقش مدیر سامانه قابل اعطا است.");
                }
            }
            else if (!PermissionCatalog.IsAssignable(permission))
            {
                throw new ValidationAppException($"کلید مجوز «{permission}» در کاتالوگ معتبر نیست.");
            }

            result.Add((permission, assignment.Effect));
        }

        return result;
    }

    private UserDetailsDto ToDetails(
        ApplicationUser user,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> roles,
        IReadOnlyList<PermissionAssignmentDto> permissions) => new(
        user.Id,
        user.Username,
        user.FirstName,
        user.LastName,
        user.MobilePhone,
        user.Email,
        user.OrganizationalUnit,
        user.IsDisabled,
        user.IsDirectoryUser,
        user.IsLocalAdmin,
        user.CreatedAtUtc,
        user.LastDirectorySyncAtUtc,
        roleIds,
        roles,
        permissions);
}
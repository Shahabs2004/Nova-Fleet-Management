using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Identity;

/// <summary>مدیریت نقش‌های پایه و سفارشی، مجوزهای آن‌ها و نگاشت گروه‌های Active Directory.</summary>
public sealed class RoleService(
    NovaFleetDbContext db,
    IAuditTrail audit) : IRoleService
{
    private const string SystemAdministrator = "SystemAdministrator";
    private static readonly Regex RoleNamePattern = new("^[A-Za-z][A-Za-z0-9]{2,49}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(item => item.Title).ToListAsync(cancellationToken);
        var permissions = await db.RolePermissions.AsNoTracking().ToListAsync(cancellationToken);
        var counts = await db.UserRoles.AsNoTracking()
            .GroupBy(item => item.RoleId)
            .Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return roles.Select(role => new RoleDto(
            role.Id,
            role.Name,
            role.Title,
            role.Description,
            role.IsSystem,
            role.IsActive,
            counts.FirstOrDefault(item => item.RoleId == role.Id)?.Count ?? 0,
            permissions.Where(item => item.RoleId == role.Id)
                .Select(item => new PermissionAssignmentDto(item.Permission, item.Effect))
                .ToList())).ToList();
    }

    public async Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (!RoleNamePattern.IsMatch(name))
        {
            throw new ValidationAppException("نام فنی نقش باید با حرف شروع شود و ۳ تا ۵۰ نویسه انگلیسی/رقمی باشد.");
        }

        if (await db.Roles.AnyAsync(item => item.Name == name, cancellationToken))
        {
            throw new ConflictAppException("نقشی با این نام فنی وجود دارد.");
        }

        var role = new Role(name, request.Title, request.Description);
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("CreateRole", "Role", role.Id.ToString(), "Success", After: new { role.Name }), cancellationToken);

        return await GetDtoAsync(role.Id, cancellationToken);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = await LoadAsync(id, cancellationToken);
        role.Update(request.Title, request.Description);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("UpdateRole", "Role", id.ToString(), "Success"), cancellationToken);
        return await GetDtoAsync(id, cancellationToken);
    }

    public async Task<RoleDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var role = await LoadAsync(id, cancellationToken);
        if (role.IsSystem && !active)
        {
            throw new ValidationAppException("نقش سیستمی قابل غیرفعال‌سازی نیست.");
        }

        if (active)
        {
            role.Enable();
        }
        else
        {
            role.Disable();
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord(active ? "EnableRole" : "DisableRole", "Role", id.ToString(), "Success"), cancellationToken);
        return await GetDtoAsync(id, cancellationToken);
    }

    public async Task SetPermissionsAsync(Guid id, SetPermissionsRequest request, CancellationToken cancellationToken = default)
    {
        var role = await LoadAsync(id, cancellationToken);
        var normalized = new List<PermissionAssignmentDto>();

        foreach (var assignment in request.Permissions)
        {
            var permission = assignment.Permission?.Trim() ?? string.Empty;
            if (permission == PermissionCatalog.All)
            {
                if (!string.Equals(role.Name, SystemAdministrator, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationAppException("مجوز فراگیر فقط برای نقش مدیر سامانه مجاز است.");
                }
            }
            else if (!PermissionCatalog.IsAssignable(permission))
            {
                throw new ValidationAppException($"کلید مجوز «{permission}» در کاتالوگ معتبر نیست.");
            }

            normalized.Add(new PermissionAssignmentDto(permission, assignment.Effect));
        }

        var existing = await db.RolePermissions.Where(item => item.RoleId == id).ToListAsync(cancellationToken);
        db.RolePermissions.RemoveRange(existing);
        db.RolePermissions.AddRange(normalized.Select(item => new RolePermission(id, item.Permission, item.Effect)));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("SetRolePermissions", "Role", id.ToString(), "Success", After: normalized), cancellationToken);
    }

    public async Task<IReadOnlyList<DirectoryMappingDto>> ListDirectoryMappingsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
                from mapping in db.DirectoryGroupRoleMappings.AsNoTracking()
                join role in db.Roles.AsNoTracking() on mapping.RoleId equals role.Id
                orderby mapping.GroupName
                select new DirectoryMappingDto(mapping.Id, mapping.GroupName, role.Id, role.Title, mapping.IsActive))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<DirectoryMappingDto> SaveDirectoryMappingAsync(
        SaveDirectoryMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GroupName, nameof(request.GroupName));
        var groupName = request.GroupName.Trim();
        var role = await LoadAsync(request.RoleId, cancellationToken);

        var mapping = await db.DirectoryGroupRoleMappings
            .FirstOrDefaultAsync(item => item.GroupName == groupName, cancellationToken);

        if (mapping is null)
        {
            mapping = new DirectoryGroupRoleMapping(groupName, role.Id);
            db.DirectoryGroupRoleMappings.Add(mapping);
        }
        else
        {
            // نگاشت موجود با نقش جدید جایگزین می‌شود چون دامنه فقط در زمان ساخت قابل تنظیم است.
            db.DirectoryGroupRoleMappings.Remove(mapping);
            mapping = new DirectoryGroupRoleMapping(groupName, role.Id);
            db.DirectoryGroupRoleMappings.Add(mapping);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("SaveDirectoryMapping", "DirectoryGroupRoleMapping", mapping.Id.ToString(), "Success", After: new { groupName, role = role.Name }), cancellationToken);
        return new DirectoryMappingDto(mapping.Id, mapping.GroupName, role.Id, role.Title, mapping.IsActive);
    }

    public async Task RemoveDirectoryMappingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var mapping = await db.DirectoryGroupRoleMappings.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundAppException("نگاشت گروه پیدا نشد.");

        db.DirectoryGroupRoleMappings.Remove(mapping);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("RemoveDirectoryMapping", "DirectoryGroupRoleMapping", id.ToString(), "Success"), cancellationToken);
    }

    private async Task<Role> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Roles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
        ?? throw new NotFoundAppException("نقش پیدا نشد.");

    private async Task<RoleDto> GetDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await LoadAsync(id, cancellationToken);
        var permissions = await db.RolePermissions.AsNoTracking()
            .Where(item => item.RoleId == id)
            .Select(item => new PermissionAssignmentDto(item.Permission, item.Effect))
            .ToListAsync(cancellationToken);
        var userCount = await db.UserRoles.CountAsync(item => item.RoleId == id, cancellationToken);

        return new RoleDto(role.Id, role.Name, role.Title, role.Description, role.IsSystem, role.IsActive, userCount, permissions);
    }
}
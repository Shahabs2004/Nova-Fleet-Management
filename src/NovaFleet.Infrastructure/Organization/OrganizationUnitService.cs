using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Organization;
using NovaFleet.Domain.Organization;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Organization;

/// <summary>واحدهای سازمانی درخواست‌کننده‌ها؛ فقط یک واحد با هر نام.</summary>
public sealed class OrganizationUnitService(
    NovaFleetDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit) : IOrganizationUnitService
{
    public Task<IReadOnlyList<OrganizationUnitDto>> ListAsync(CancellationToken cancellationToken = default)
        => ListInternalAsync(includeInactive: true, cancellationToken);

    public async Task<OrganizationUnitDto> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.SystemManage);
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        var normalized = name.Trim();
        if (await db.OrganizationUnits.AnyAsync(item => item.Name == normalized, cancellationToken))
        {
            throw new ConflictAppException("واحدی با این نام وجود دارد.");
        }

        var unit = new OrganizationUnit(normalized);
        db.OrganizationUnits.Add(unit);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("CreateOrganizationUnit", "OrganizationUnit", unit.Id.ToString(), "Success", After: new { unit.Name }), cancellationToken);
        return new OrganizationUnitDto(unit.Id, unit.Name, unit.IsActive);
    }

    public async Task<OrganizationUnitDto> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.SystemManage);
        var unit = await LoadAsync(id, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        if (await db.OrganizationUnits.AnyAsync(item => item.Name == name.Trim() && item.Id != id, cancellationToken))
        {
            throw new ConflictAppException("واحدی با این نام وجود دارد.");
        }

        unit.Rename(name);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("RenameOrganizationUnit", "OrganizationUnit", id.ToString(), "Success", After: new { unit.Name }), cancellationToken);
        return new OrganizationUnitDto(unit.Id, unit.Name, unit.IsActive);
    }

    public async Task<OrganizationUnitDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.SystemManage);
        var unit = await LoadAsync(id, cancellationToken);
        if (active)
        {
            unit.Enable();
        }
        else
        {
            unit.Disable();
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord(active ? "EnableOrganizationUnit" : "DisableOrganizationUnit", "OrganizationUnit", id.ToString(), "Success"), cancellationToken);
        return new OrganizationUnitDto(unit.Id, unit.Name, unit.IsActive);
    }

    private async Task<IReadOnlyList<OrganizationUnitDto>> ListInternalAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        currentUser.RequireAny(PermissionCatalog.Administration.SystemManage, PermissionCatalog.Missions.Create, PermissionCatalog.Administration.UsersManage);
        var source = db.OrganizationUnits.AsNoTracking();
        if (!includeInactive)
        {
            source = source.Where(item => item.IsActive);
        }

        return await source.OrderBy(item => item.Name)
            .Select(item => new OrganizationUnitDto(item.Id, item.Name, item.IsActive))
            .ToListAsync(cancellationToken);
    }

    private async Task<OrganizationUnit> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.OrganizationUnits.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
        ?? throw new NotFoundAppException("واحد سازمانی پیدا نشد.");
}

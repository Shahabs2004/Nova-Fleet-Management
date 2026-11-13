using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Drivers;
using NovaFleet.Domain.Drivers;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Drivers;

/// <summary>مدیریت پرونده رانندگان؛ وضعیت همکاری و خودروی پیش‌فرض با کنترل مجوز جداگانه.</summary>
public sealed class DriverService(
    NovaFleetDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit) : IDriverService
{
    public async Task<PagedResult<DriverDto>> SearchAsync(
        string? search,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Drivers.View);
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);

        var source = db.Drivers.AsNoTracking();
        if (!includeArchived)
        {
            source = source.Where(item => !item.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            source = source.Where(item =>
                item.FirstName.Contains(term)
                || item.LastName.Contains(term)
                || item.MobilePhone.Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source
            .OrderBy(item => item.LastName).ThenBy(item => item.FirstName)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .Select(item => new { item, Plate = db.Vehicles.Where(v => v.Id == item.DefaultVehicleId).Select(v => v.PlateDisplay).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => ToDto(row.item, row.Plate)).ToList();
        return new PagedResult<DriverDto>(items, total, safePage, safeSize);
    }

    public async Task<IReadOnlyList<DriverOptionDto>> ListOptionsAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Drivers.View);
        var source = db.Drivers.AsNoTracking().Where(item => !item.IsArchived);
        if (!includeInactive)
        {
            source = source.Where(item => item.EmploymentStatus == DriverEmploymentStatus.Active);
        }

        return await source
            .OrderBy(item => item.LastName).ThenBy(item => item.FirstName)
            .Select(item => new DriverOptionDto(item.Id, item.FirstName + " " + item.LastName, item.MobilePhone, item.DefaultVehicleId))
            .ToListAsync(cancellationToken);
    }

    public async Task<DriverDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Drivers.View);
        var driver = await LoadAsync(id, cancellationToken);
        var plate = driver.DefaultVehicleId.HasValue
            ? await db.Vehicles.Where(item => item.Id == driver.DefaultVehicleId).Select(item => item.PlateDisplay).FirstOrDefaultAsync(cancellationToken)
            : null;
        return ToDto(driver, plate);
    }

    public async Task<DriverDto> CreateAsync(DriverCreateRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Drivers.Manage);
        var driver = new Driver(request.FirstName, request.LastName, request.MobilePhone);
        db.Drivers.Add(driver);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("CreateDriver", "Driver", driver.Id.ToString(), "Success", After: new { driver.DisplayName, driver.MobilePhone }), cancellationToken);
        return ToDto(driver, null);
    }

    public async Task<DriverDto> UpdateAsync(Guid id, DriverUpdateRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Drivers.Manage);
        var driver = await LoadAsync(id, cancellationToken);
        db.ApplyRowVersion(driver, request.RowVersion);
        driver.UpdateProfile(request.FirstName, request.LastName, request.MobilePhone);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("UpdateDriver", "Driver", id.ToString(), "Success", After: new { driver.DisplayName, driver.MobilePhone }), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<DriverDto> SetEmploymentStatusAsync(
        Guid id,
        DriverEmploymentStatus status,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Drivers.Manage);
        var driver = await LoadAsync(id, cancellationToken);
        db.ApplyRowVersion(driver, rowVersion);
        driver.ChangeEmploymentStatus(status);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("ChangeDriverStatus", "Driver", id.ToString(), "Success", After: new { status }), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<DriverDto> SetDefaultVehicleAsync(
        Guid id,
        Guid? vehicleId,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Drivers.DefaultVehicleManage, PermissionCatalog.Drivers.Manage);
        var driver = await LoadAsync(id, cancellationToken);
        db.ApplyRowVersion(driver, rowVersion);

        if (vehicleId.HasValue)
        {
            var vehicle = await db.Vehicles.FirstOrDefaultAsync(item => item.Id == vehicleId, cancellationToken)
                ?? throw new NotFoundAppException("خودروی انتخاب‌شده پیدا نشد.");
            if (vehicle.IsArchived)
            {
                throw new ValidationAppException("خودروی بایگانی‌شده نمی‌تواند پیش‌فرض باشد.");
            }

            var otherHolders = await db.Drivers
                .Where(item => item.DefaultVehicleId == vehicleId && item.Id != id)
                .ToListAsync(cancellationToken);
            foreach (var holder in otherHolders)
            {
                holder.SetDefaultVehicle(null);
            }
        }

        driver.SetDefaultVehicle(vehicleId);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("SetDefaultVehicle", "Driver", id.ToString(), "Success", After: new { vehicleId }), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task SetVehicleDriverAsync(Guid vehicleId, Guid? driverId, string? rowVersion, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Drivers.DefaultVehicleManage, PermissionCatalog.Drivers.Manage);
        if (driverId.HasValue)
        {
            await SetDefaultVehicleAsync(driverId.Value, vehicleId, rowVersion, cancellationToken);
            return;
        }

        var holders = await db.Drivers.Where(item => item.DefaultVehicleId == vehicleId).ToListAsync(cancellationToken);
        foreach (var holder in holders)
        {
            holder.SetDefaultVehicle(null);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("SetVehicleDriver", "Vehicle", vehicleId.ToString(), "Success", After: new { driverId }), cancellationToken);
    }

    public async Task<DriverDto> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Drivers.Manage);
        var driver = await LoadAsync(id, cancellationToken);
        if (archived)
        {
            driver.Archive();
        }
        else
        {
            driver.Reactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord(archived ? "ArchiveDriver" : "ReactivateDriver", "Driver", id.ToString(), "Success"), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<Driver> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Drivers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
        ?? throw new NotFoundAppException("راننده پیدا نشد.");

    private static DriverDto ToDto(Driver driver, string? plate) => new(
        driver.Id,
        driver.FirstName,
        driver.LastName,
        driver.DisplayName,
        driver.MobilePhone,
        driver.EmploymentStatus,
        driver.DefaultVehicleId,
        plate,
        driver.IsArchived,
        driver.CreatedAtUtc,
        driver.RowVersion.ToRowVersionString());
}

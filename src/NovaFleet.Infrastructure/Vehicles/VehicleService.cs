using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Vehicles;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Vehicles;

/// <summary>مدیریت خودروها و پلاک ایرانی؛ وضعیت مؤثر بر اساس مأموریت فعال محاسبه می‌شود.</summary>
public sealed class VehicleService(
    NovaFleetDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit) : IVehicleService
{
    private static readonly MissionStatus[] ActiveStatuses =
    [
        MissionStatus.Confirmed, MissionStatus.Running, MissionStatus.WithdrawalPending
    ];

    public async Task<PagedResult<VehicleDto>> SearchAsync(
        string? search,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Vehicles.View);
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);

        var source = db.Vehicles.AsNoTracking();
        if (!includeArchived)
        {
            source = source.Where(item => !item.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            source = source.Where(item =>
                item.Brand.Contains(term)
                || item.Model.Contains(term)
                || item.PlateFirstTwo.Contains(term)
                || item.PlateNextThree.Contains(term)
                || item.PlateIranCode.Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var activeVehicleIds = await db.Missions.AsNoTracking()
            .Where(item => item.VehicleId != null && ActiveStatuses.Contains(item.Status))
            .Select(item => item.VehicleId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var rows = await source
            .OrderBy(item => item.Brand).ThenBy(item => item.Model)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(vehicle => ToDto(vehicle, activeVehicleIds.Contains(vehicle.Id))).ToList();
        return new PagedResult<VehicleDto>(items, total, safePage, safeSize);
    }

    public async Task<IReadOnlyList<VehicleOptionDto>> ListOptionsAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Vehicles.View);
        var source = db.Vehicles.AsNoTracking();
        if (!includeArchived)
        {
            source = source.Where(item => !item.IsArchived);
        }

        var activeVehicleIds = await db.Missions.AsNoTracking()
            .Where(item => item.VehicleId != null && ActiveStatuses.Contains(item.Status))
            .Select(item => item.VehicleId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var vehicles = await source.OrderBy(item => item.Brand).ToListAsync(cancellationToken);
        return vehicles.Select(vehicle => new VehicleOptionDto(
            vehicle.Id,
            vehicle.DisplayName,
            vehicle.PlateDisplay,
            vehicle.GetEffectiveStatus(activeVehicleIds.Contains(vehicle.Id)),
            vehicle.ManualStatus)).ToList();
    }

    public async Task<VehicleDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Vehicles.View);
        var vehicle = await LoadAsync(id, cancellationToken);
        var hasActive = await HasActiveMissionAsync(id, cancellationToken);
        return ToDto(vehicle, hasActive);
    }

    public async Task<VehicleDto> CreateAsync(VehicleCreateRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Vehicles.Manage);
        var plate = BuildPlate(request.PlateFirstTwo, request.PlateLetter, request.PlateNextThree, request.PlateIranCode);
        var vehicle = new Vehicle(request.Brand, request.Model, plate);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("CreateVehicle", "Vehicle", vehicle.Id.ToString(), "Success", After: new { vehicle.DisplayName }), cancellationToken);
        return ToDto(vehicle, false);
    }

    public async Task<VehicleDto> UpdateAsync(Guid id, VehicleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Vehicles.Manage);
        var vehicle = await LoadAsync(id, cancellationToken);
        db.ApplyRowVersion(vehicle, request.RowVersion);
        var plate = BuildPlate(request.PlateFirstTwo, request.PlateLetter, request.PlateNextThree, request.PlateIranCode);
        vehicle.UpdateDetails(request.Brand, request.Model, plate);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("UpdateVehicle", "Vehicle", id.ToString(), "Success", After: new { vehicle.DisplayName }), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<VehicleDto> SetManualStatusAsync(
        Guid id,
        ManualVehicleStatus status,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Vehicles.Manage);
        var vehicle = await LoadAsync(id, cancellationToken);
        db.ApplyRowVersion(vehicle, rowVersion);
        vehicle.ChangeManualStatus(status, await HasActiveMissionAsync(id, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("ChangeVehicleStatus", "Vehicle", id.ToString(), "Success", After: new { status }), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<VehicleDto> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Vehicles.Manage);
        var vehicle = await LoadAsync(id, cancellationToken);
        if (archived)
        {
            if (await HasActiveMissionAsync(id, cancellationToken))
            {
                throw new ConflictAppException("خودروی دارای مأموریت فعال قابل بایگانی نیست.");
            }

            vehicle.Archive();
        }
        else
        {
            vehicle.Reactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord(archived ? "ArchiveVehicle" : "ReactivateVehicle", "Vehicle", id.ToString(), "Success"), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private static IranianLicensePlate BuildPlate(string firstTwo, string letter, string nextThree, string iranCode)
    {
        try
        {
            return IranianLicensePlate.Create(firstTwo, letter, nextThree, iranCode);
        }
        catch (ArgumentException ex)
        {
            var message = ex.Message;
            var parameterSuffix = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
            if (parameterSuffix >= 0)
            {
                message = message[..parameterSuffix];
            }

            throw new ValidationAppException(message);
        }
    }

    private async Task<Vehicle> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Vehicles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
        ?? throw new NotFoundAppException("خودرو پیدا نشد.");

    private Task<bool> HasActiveMissionAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        db.Missions.AnyAsync(item => item.VehicleId == vehicleId && ActiveStatuses.Contains(item.Status), cancellationToken);

    private static VehicleDto ToDto(Vehicle vehicle, bool hasActiveMission) => new(
        vehicle.Id,
        vehicle.Brand,
        vehicle.Model,
        vehicle.DisplayName,
        vehicle.LicensePlate.ToString().Replace(" ", string.Empty),
        vehicle.PlateDisplay,
        vehicle.PlateFirstTwo,
        vehicle.PlateLetter,
        vehicle.PlateNextThree,
        vehicle.PlateIranCode,
        vehicle.ManualStatus,
        vehicle.GetEffectiveStatus(hasActiveMission),
        vehicle.IsArchived,
        vehicle.CreatedAtUtc,
        vehicle.RowVersion.ToRowVersionString());
}

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Maintenance;
using NovaFleet.Domain.Maintenance;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Maintenance;

public sealed partial class MaintenanceService(NovaFleetDbContext db, ICurrentUser user, IClock clock) : IMaintenanceService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private void View() => user.Require(PermissionCatalog.Maintenance.View);

    private async Task<Vehicle> LoadVehicleAsync(Guid id, CancellationToken ct, bool creating = false)
    {
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NotFoundAppException("خودرو پیدا نشد.");
        if (creating && vehicle.IsArchived) throw new ValidationAppException("ثبت جدید برای خودروی بایگانی‌شده ممکن نیست.");
        return vehicle;
    }

    private static void Version(int actual, int expected)
    {
        if (expected < 1) throw new ValidationAppException("نسخهٔ رکورد الزامی است.");
        if (actual != expected) throw new ConflictAppException("رکورد تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }

    private void Past(DateTimeOffset value)
    {
        if (value.Year < 1900 || value > clock.UtcNow)
            throw new ValidationAppException("زمان ثبت باید معتبر و در گذشته یا اکنون باشد.");
    }

    private void Revision(string kind, Guid id, Guid vehicleId, int version, object before, string? reason)
    {
        db.MaintenanceRevisions.Add(new MaintenanceRevision { EntityKind = kind, EntityId = id,
            VehicleId = vehicleId, Version = version, BeforeJson = JsonSerializer.Serialize(before, Json),
            Reason = MaintenanceRules.Required(reason, 500, "دلیل اصلاح"), Actor = user.Username });
    }

    public async Task<IReadOnlyList<RevisionDto>> RevisionsAsync(Guid vehicleId, Guid entityId, CancellationToken ct)
    {
        View();
        await LoadVehicleAsync(vehicleId, ct);
        return await db.MaintenanceRevisions.AsNoTracking().Where(r => r.VehicleId == vehicleId && r.EntityId == entityId)
            .OrderByDescending(r => r.CreatedAtUtc).Select(r => new RevisionDto(r.Id, r.EntityKind, r.EntityId,
                r.Version, r.BeforeJson, r.Reason, r.Actor, r.CreatedAtUtc)).ToListAsync(ct);
    }

    public async Task<OdometerSummary> OdometerAsync(Guid vehicleId, CancellationToken ct)
    {
        View();
        await LoadVehicleAsync(vehicleId, ct);
        var readings = await db.VehicleOdometerReadings.AsNoTracking().Where(r => r.VehicleId == vehicleId).ToListAsync(ct);
        return SummarizeOdometer(readings);
    }

    private OdometerSummary SummarizeOdometer(IReadOnlyList<VehicleOdometerReading> readings)
    {
        var conflicts = OdometerLedger.ConflictIds(readings);
        var latest = readings.Where(r => !r.IsVoided && r.ObservedAtUtc <= clock.UtcNow)
            .OrderByDescending(r => r.ObservedAtUtc).ThenByDescending(r => r.Kilometers).FirstOrDefault();
        return new OdometerSummary(conflicts.Count == 0 ? latest?.Kilometers : null, latest?.ObservedAtUtc,
            latest?.IsTimeEstimated ?? false, conflicts.Count > 0,
            readings.OrderByDescending(r => r.ObservedAtUtc).ThenByDescending(r => r.Kilometers).Select(r => ToOdometer(r, conflicts.Contains(r.Id))).ToList());
    }

    private static OdometerDto ToOdometer(VehicleOdometerReading r, bool conflict = false) =>
        new(r.Id, r.SourceKind, r.SourceId, r.ObservedAtUtc, r.IsTimeEstimated, r.Kilometers,
            r.IsVoided, r.RecordedBy, r.Version, conflict);

    public async Task<OdometerDto> SaveOdometerAsync(Guid vehicleId, Guid? id, OdometerInput input, CancellationToken ct)
    {
        View();
        user.Require(id.HasValue ? PermissionCatalog.Maintenance.OdometerCorrect : PermissionCatalog.Maintenance.OdometerCreate);
        await LoadVehicleAsync(vehicleId, ct, !id.HasValue);
        Past(input.ObservedAtUtc);
        MaintenanceRules.ValidateKm(input.Kilometers);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await OdometerLedger.LockVehicleAsync(db, vehicleId, ct);
        if (!id.HasValue)
        {
            if (input.RequestId == Guid.Empty) throw new ValidationAppException("شناسهٔ درخواست الزامی است.");
            var duplicate = await db.VehicleOdometerReadings.SingleOrDefaultAsync(r => r.SourceKind == "manual" && r.SourceId == input.RequestId, ct);
            if (duplicate is not null)
            {
                if (duplicate.VehicleId != vehicleId || duplicate.RecordedBy != user.Username || duplicate.Kilometers != input.Kilometers || duplicate.ObservedAtUtc != input.ObservedAtUtc)
                    throw new ConflictAppException("شناسهٔ درخواست قبلاً استفاده شده است.");
                return ToOdometer(duplicate);
            }
        }
        var row = id.HasValue ? await db.VehicleOdometerReadings.SingleOrDefaultAsync(r => r.Id == id && r.VehicleId == vehicleId, ct)
            ?? throw new NotFoundAppException("قرائت پیدا نشد.")
            : new VehicleOdometerReading { VehicleId = vehicleId, SourceId = input.RequestId, RecordedBy = user.Username };
        if (id.HasValue)
        {
            Version(row.Version, input.Version);
            if (row.SourceKind == "maintenance")
                throw new ValidationAppException("کیلومتر این قرائت را از فرم اصلاح سرویس تغییر دهید.");
            if (row.SourceKind.StartsWith("mission-", StringComparison.Ordinal))
            {
                var reason = MaintenanceRules.Required(input.Reason, 500, "دلیل اصلاح");
                if (input.ObservedAtUtc != row.ObservedAtUtc)
                    throw new ValidationAppException("تاریخ قرائت مأموریت از زمان خود مأموریت پیروی می‌کند.");
                var mission = await db.Missions.SingleAsync(m => m.Id == row.SourceId && m.VehicleId == vehicleId, ct);
                if (row.SourceKind == "mission-start") mission.SetOdometerStart(input.Kilometers);
                else mission.SetOdometerEnd(input.Kilometers);
                db.OdometerActor = user.Username;
                db.OdometerChangeReason = reason;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return ToOdometer(row);
            }
            Revision("odometer", row.Id, vehicleId, row.Version, ToOdometer(row), input.Reason);
            row.Version++;
        }
        else db.VehicleOdometerReadings.Add(row);
        row.ObservedAtUtc = input.ObservedAtUtc;
        row.Kilometers = input.Kilometers;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToOdometer(row);
    }
}

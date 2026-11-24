using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Common;
using NovaFleet.Domain.Maintenance;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Maintenance;

internal static class OdometerLedger
{
    private static readonly string[] SourceProperties = [nameof(Mission.VehicleId), nameof(Mission.OdometerStartKm),
        nameof(Mission.OdometerEndKm), nameof(Mission.ActualStartAtUtc), nameof(Mission.ActualEndAtUtc),
        nameof(Mission.PlannedStartAtUtc), nameof(Mission.PlannedEndAtUtc)];

    public static bool NeedsSynchronization(NovaFleetDbContext db) =>
        db.ChangeTracker.Entries<VehicleOdometerReading>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
        || ChangedMissions(db).Count > 0;

    private static List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Mission>> ChangedMissions(NovaFleetDbContext db) =>
        db.ChangeTracker.Entries<Mission>().Where(e =>
            e.State == EntityState.Added && (e.Entity.OdometerStartKm.HasValue || e.Entity.OdometerEndKm.HasValue)
            || e.State == EntityState.Modified && SourceProperties.Any(p => e.Property(p).IsModified)
            || e.State == EntityState.Deleted).ToList();

    public static async Task SynchronizeAsync(NovaFleetDbContext db, CancellationToken ct)
    {
        var missions = ChangedMissions(db);
        var vehicleIds = missions.Select(e => e.Entity.VehicleId)
            .Concat(missions.Where(e => e.State != EntityState.Added).Select(e => e.Property(x => x.VehicleId).OriginalValue))
            .Concat(db.ChangeTracker.Entries<VehicleOdometerReading>().Where(e => e.State != EntityState.Unchanged).Select(e => (Guid?)e.Entity.VehicleId))
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().Order().ToArray();
        foreach (var vehicleId in vehicleIds)
            await LockVehicleAsync(db, vehicleId, ct);

        var before = await db.VehicleOdometerReadings.AsNoTracking().Where(r => vehicleIds.Contains(r.VehicleId)).ToListAsync(ct);
        foreach (var entry in missions)
        {
            var mission = entry.Entity;
            var existing = await db.VehicleOdometerReadings.Where(r => r.SourceId == mission.Id &&
                (r.SourceKind == "mission-start" || r.SourceKind == "mission-end")).ToListAsync(ct);
            foreach (var kind in new[] { "mission-start", "mission-end" })
            {
                var start = kind == "mission-start";
                var km = start ? mission.OdometerStartKm : mission.OdometerEndKm;
                var actual = start ? mission.ActualStartAtUtc : mission.ActualEndAtUtc;
                var date = actual ?? (start ? mission.PlannedStartAtUtc : mission.PlannedEndAtUtc);
                var row = existing.SingleOrDefault(r => r.SourceKind == kind);
                var voided = km is null || mission.VehicleId is null || entry.State == EntityState.Deleted;
                if (row is null)
                {
                    if (voided) continue;
                    row = new VehicleOdometerReading { SourceKind = kind, SourceId = mission.Id, RecordedBy = db.OdometerActor };
                    db.VehicleOdometerReadings.Add(row);
                }
                else if (row.Kilometers == km && row.ObservedAtUtc == date && row.VehicleId == mission.VehicleId
                    && row.IsTimeEstimated == !actual.HasValue && row.IsVoided == voided)
                    continue;
                else
                {
                    db.MaintenanceRevisions.Add(new MaintenanceRevision {
                        EntityKind = "odometer", EntityId = row.Id, VehicleId = row.VehicleId, Version = row.Version,
                        BeforeJson = JsonSerializer.Serialize(row), Actor = db.OdometerActor, Reason = db.OdometerChangeReason });
                    row.Version++;
                }
                row.VehicleId = mission.VehicleId ?? row.VehicleId;
                row.Kilometers = km ?? row.Kilometers;
                row.ObservedAtUtc = date;
                row.IsTimeEstimated = !actual.HasValue;
                row.IsVoided = voided;
            }
        }

        var changed = db.ChangeTracker.Entries<VehicleOdometerReading>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var changedIds = changed.Select(e => e.Entity.Id).ToHashSet();
        var after = before.Where(r => !changedIds.Contains(r.Id)).Concat(changed.Where(e => e.State != EntityState.Deleted).Select(e => e.Entity)).ToList();
        foreach (var vehicleId in vehicleIds)
        {
            var previous = Conflicts(before.Where(r => r.VehicleId == vehicleId));
            var current = Conflicts(after.Where(r => r.VehicleId == vehicleId));
            var introducesConflict = current.Ids.Except(previous.Ids).Any();
            var repairsHistory = current.Severity < previous.Severity && !introducesConflict;
            var preservesHistory = current.Ids.SetEquals(previous.Ids) && current.Severity == previous.Severity
                && !current.Ids.Overlaps(changedIds);
            if (current.Ids.Count > 0 && !repairsHistory && !preservesHistory)
                throw new ValidationAppException("کیلومتر با سوابق قبل یا بعد خودرو سازگار نیست؛ ناسازگاری‌های دفترچه باید توسط مدیر اصلاح شوند.");
        }
    }

    public static Task LockVehicleAsync(NovaFleetDbContext db, Guid vehicleId, CancellationToken ct) =>
        ResourceLocks.LockVehicleAsync(db, vehicleId, ct);

    public static HashSet<Guid> ConflictIds(IEnumerable<VehicleOdometerReading> readings) => Conflicts(readings).Ids;

    private static (HashSet<Guid> Ids, decimal Severity) Conflicts(IEnumerable<VehicleOdometerReading> readings)
    {
        var conflicts = new HashSet<Guid>();
        decimal severity = 0;
        VehicleOdometerReading? previous = null;
        foreach (var group in readings.Where(r => !r.IsVoided).GroupBy(r => r.ObservedAtUtc).OrderBy(g => g.Key))
        {
            var min = group.MinBy(r => r.Kilometers)!;
            var max = group.MaxBy(r => r.Kilometers)!;
            // Service readings carry a date-only timestamp (the time of service is irrelevant), so
            // several visits sharing a timestamp are a km-ordered progression, not simultaneous
            // conflicting readings. Only a real-time reading anchors a genuine instant where differing
            // km is a true conflict. Cross-timestamp backwards detection below is unchanged.
            if (min.Kilometers != max.Kilometers && group.Any(r => r.SourceKind != "maintenance"))
            {
                severity += (decimal)max.Kilometers - min.Kilometers;
                foreach (var reading in group) conflicts.Add(reading.Id);
            }
            if (previous is not null && min.Kilometers < previous.Kilometers)
            {
                conflicts.Add(previous.Id);
                conflicts.Add(min.Id);
                severity += (decimal)previous.Kilometers - min.Kilometers;
            }
            if (previous is null || max.Kilometers >= previous.Kilometers) previous = max;
        }
        return (conflicts, severity);
    }
}

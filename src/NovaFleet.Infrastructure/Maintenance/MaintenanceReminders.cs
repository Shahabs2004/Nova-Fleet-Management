using System.Data;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Domain.Maintenance;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Maintenance;

public sealed class MaintenanceReminders(NovaFleetDbContext db, IPermissionResolver permissions, IClock clock)
{
    public async Task<int> SendAsync(CancellationToken ct)
    {
        var managers = await permissions.ListUsernamesWithPermissionAsync(PermissionCatalog.Maintenance.Manage, ct);
        var viewers = await permissions.ListUsernamesWithPermissionAsync(PermissionCatalog.Maintenance.View, ct);
        var recipients = managers.Intersect(viewers, StringComparer.OrdinalIgnoreCase).ToArray();
        if (recipients.Length == 0) return 0;
        var ids = await db.MaintenanceSchedules.AsNoTracking().Where(s => s.IsActive).Select(s => s.VehicleId).Distinct().ToListAsync(ct);
        var service = new MaintenanceService(db, AnonymousCurrentUser.Instance, clock);
        var sent = 0;
        foreach (var id in ids.Order())
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await OdometerLedger.LockVehicleAsync(db, id, ct);
            var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id && !v.IsArchived, ct);
            if (vehicle is null) continue;
            var summary = (await service.BuildVehiclesAsync([vehicle], ct))[0];
            var existing = await db.MaintenanceReminderDeliveries.AsNoTracking()
                .Where(d => db.MaintenanceSchedules.Any(s => s.Id == d.ScheduleId && s.VehicleId == id)).ToListAsync(ct);
            foreach (var schedule in summary.Schedules.Where(s => s.State is "due" or "approaching"))
            foreach (var recipient in recipients)
            {
                if (existing.Any(d => d.ScheduleId == schedule.Id && d.CycleKey == schedule.CycleKey && d.Recipient == recipient
                    && (d.Stage == schedule.State || schedule.State == "approaching" && d.Stage == "due"))) continue;
                var title = schedule.State == "due" ? "موعد سرویس خودرو رسیده است" : "سرویس خودرو نزدیک است";
                db.UserNotifications.Add(new UserNotification(recipient, title,
                    $"{summary.DisplayName}، {summary.Plate} — {schedule.Name}", "maintenance."+schedule.State, "maintenance", id.ToString()));
                db.MaintenanceReminderDeliveries.Add(new MaintenanceReminderDelivery {
                    ScheduleId = schedule.Id, CycleKey = schedule.CycleKey, Stage = schedule.State, Recipient = recipient });
                sent++;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return sent;
    }
}

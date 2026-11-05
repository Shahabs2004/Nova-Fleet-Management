using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Dashboard;
using NovaFleet.Application.Missions;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Dashboard;

/// <summary>داشبورد متناسب با نقش؛ کارت‌های خارج از مجوز کاربر خالی یا صفر می‌مانند.</summary>
public sealed class DashboardService(NovaFleetDbContext db, ICurrentUser currentUser, IClock clock) : IDashboardService
{
    public async Task<DashboardStatsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Dashboard.View);
        var now = clock.UtcNow;

        var pendingRequests = 0;
        var activeMissions = 0;
        var pendingWithdrawals = 0;
        var pendingChangeRequests = 0;
        var missionsThisMonth = 0;
        var distanceThisMonthKm = 0L;
        IReadOnlyList<MissionListItemDto> upcoming = [];

        if (currentUser.Has(PermissionCatalog.Missions.View) || currentUser.Has(PermissionCatalog.Missions.ViewOwn))
        {
            pendingRequests = await db.Missions.CountAsync(item => item.Status == MissionStatus.Pending || item.Status == MissionStatus.UnderReview, cancellationToken);
            activeMissions = await db.Missions.CountAsync(item => item.Status == MissionStatus.Confirmed || item.Status == MissionStatus.Running, cancellationToken);
            pendingWithdrawals = await db.Missions.CountAsync(item => item.Status == MissionStatus.WithdrawalPending, cancellationToken);
            pendingChangeRequests = await db.MissionChangeRequests.CountAsync(item => item.Status == MissionChangeRequestStatus.Pending, cancellationToken);

            var monthStart = StartOfMonthUtc(now);
            missionsThisMonth = await db.Missions.CountAsync(item => item.CreatedAtUtc >= monthStart, cancellationToken);
            distanceThisMonthKm = await db.Missions
                .Where(item => item.ActualEndAtUtc >= monthStart && item.OdometerStartKm != null && item.OdometerEndKm != null)
                .SumAsync(item => item.OdometerEndKm!.Value - item.OdometerStartKm!.Value, cancellationToken);

            upcoming = (await db.Missions.AsNoTracking()
                    .Where(item => item.Status == MissionStatus.Confirmed || item.Status == MissionStatus.Running)
                    .OrderBy(item => item.PlannedStartAtUtc)
                    .Take(5)
                    .Select(item => new
                    {
                        item,
                        DriverName = db.Drivers.Where(d => d.Id == item.DriverId).Select(d => d.FirstName + " " + d.LastName).FirstOrDefault(),
                        VehiclePlate = db.Vehicles.Where(v => v.Id == item.VehicleId).Select(v => v.PlateDisplay).FirstOrDefault()
                    })
                    .ToListAsync(cancellationToken))
                .Select(row => new MissionListItemDto(
                    row.item.Id,
                    row.item.MissionNumber,
                    row.item.Title,
                    row.item.Type,
                    row.item.Status,
                    row.item.RequesterName,
                    row.item.RequesterOrganizationalUnit,
                    row.item.Origin,
                    row.item.Destination,
                    row.item.PlannedStartAtUtc,
                    row.item.PlannedEndAtUtc,
                    row.item.ActualEndAtUtc,
                    row.item.DriverId,
                    row.DriverName,
                    row.item.VehicleId,
                    row.VehiclePlate,
                    row.item.SplitReason,
                    row.item.RelatedMissionId,
                    row.item.RequesterUsername == currentUser.Username))
                .ToList();
        }

        var readyVehicles = 0;
        IReadOnlyList<DashboardChartPointDto> vehicleStatuses = [];
        if (currentUser.Has(PermissionCatalog.Vehicles.View))
        {
            var vehicles = await db.Vehicles.AsNoTracking().Where(item => !item.IsArchived).ToListAsync(cancellationToken);
            var activeVehicleIds = await db.Missions
                .Where(item => item.Status == MissionStatus.Confirmed || item.Status == MissionStatus.Running || item.Status == MissionStatus.WithdrawalPending)
                .Select(item => item.VehicleId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
            readyVehicles = vehicles.Count(item => item.CanBeAssignedToMission(activeVehicleIds.Contains(item.Id)));
            vehicleStatuses =
            [
                new("آماده", vehicles.Count(item => item.CanBeAssignedToMission(activeVehicleIds.Contains(item.Id)))),
                new("در مأموریت", activeVehicleIds.Count),
                new("تعمیرگاه", vehicles.Count(item => item.ManualStatus == ManualVehicleStatus.RepairShop)),
                new("خارج از سرویس", vehicles.Count(item => item.ManualStatus == ManualVehicleStatus.OutOfService))
            ];
        }

        var activeDrivers = 0;
        IReadOnlyList<DashboardWorkloadDto> driverWorkload = [];
        if (currentUser.Has(PermissionCatalog.Drivers.View))
        {
            activeDrivers = await db.Drivers.CountAsync(item => !item.IsArchived && item.EmploymentStatus == DriverEmploymentStatus.Active, cancellationToken);
            var workloadRows = await db.Missions.AsNoTracking()
                .Where(item => item.DriverId != null
                    && (item.Status == MissionStatus.Confirmed || item.Status == MissionStatus.Running))
                .Select(item => db.Drivers.Where(d => d.Id == item.DriverId).Select(d => d.FirstName + " " + d.LastName).FirstOrDefault() ?? "نامشخص")
                .ToListAsync(cancellationToken);
            driverWorkload = workloadRows
                .GroupBy(name => name)
                .Select(group => new DashboardWorkloadDto(group.Key, group.Count()))
                .OrderByDescending(item => item.Value)
                .Take(6)
                .ToList();
        }

        var failedSms = 0;
        if (currentUser.Has(PermissionCatalog.Notifications.SmsView))
        {
            failedSms = await db.SmsQueueItems.CountAsync(item => item.Status == SmsQueueStatus.Failed || item.Status == SmsQueueStatus.PermanentFailure, cancellationToken);
        }

        var missionsByDay = new List<DashboardChartPointDto>();
        if (currentUser.Has(PermissionCatalog.Missions.View) || currentUser.Has(PermissionCatalog.Missions.ViewOwn))
        {
            var windowStart = JalaliDate.StartOfTehranDayUtc(JalaliDate.YearOf(now), JalaliDate.MonthOf(now), JalaliDate.DayOf(now)).AddDays(-13);
            var byDay = await db.Missions.AsNoTracking()
                .Where(item => item.PlannedStartAtUtc >= windowStart)
                .ToListAsync(cancellationToken);
            for (var day = 0; day < 14; day++)
            {
                var dayStart = windowStart.AddDays(day);
                var dayEnd = dayStart.AddDays(1);
                var label = JalaliDate.FormatDate(dayStart)[^5..];
                missionsByDay.Add(new DashboardChartPointDto(label, byDay.Count(item => item.PlannedStartAtUtc >= dayStart && item.PlannedStartAtUtc < dayEnd)));
            }
        }

        return new DashboardStatsDto(
            pendingRequests,
            activeMissions,
            readyVehicles,
            activeDrivers,
            pendingWithdrawals,
            pendingChangeRequests,
            failedSms,
            missionsThisMonth,
            distanceThisMonthKm,
            missionsByDay,
            driverWorkload,
            vehicleStatuses,
            upcoming);
    }

    private static DateTimeOffset StartOfMonthUtc(DateTimeOffset nowUtc) =>
        JalaliDate.StartOfTehranDayUtc(JalaliDate.YearOf(nowUtc), JalaliDate.MonthOf(nowUtc), 1);
}

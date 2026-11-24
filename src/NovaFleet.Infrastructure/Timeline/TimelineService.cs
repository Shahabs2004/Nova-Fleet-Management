using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Timeline;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Timeline;

public sealed class TimelineService(NovaFleetDbContext db, ICurrentUser currentUser) : ITimelineService
{
    private static readonly MissionStatus[] TimelineStatuses =
    [
        MissionStatus.Pending,
        MissionStatus.UnderReview,
        MissionStatus.Confirmed,
        MissionStatus.Running,
        MissionStatus.WithdrawalPending,
        MissionStatus.Completed
    ];

    public async Task<IReadOnlyList<TimelineMissionDto>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? driverId,
        Guid? vehicleId,
        CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(
            PermissionCatalog.Missions.TimelineView,
            PermissionCatalog.Missions.View,
            PermissionCatalog.Missions.ViewOwn);
        if (toUtc <= fromUtc)
        {
            throw new ValidationAppException("بازه زمانی تایم‌لاین معتبر نیست.");
        }

        if (toUtc - fromUtc > TimeSpan.FromDays(31))
        {
            throw new ValidationAppException("بازه تایم‌لاین نمی‌تواند بیش از ۳۱ روز باشد.");
        }

        var source = MissionVisibility.Apply(
                db.Missions.AsNoTracking(),
                currentUser.Has(PermissionCatalog.Missions.View),
                currentUser.Username)
            .Where(item => TimelineStatuses.Contains(item.Status)
                && item.PlannedStartAtUtc <= toUtc
                && item.PlannedEndAtUtc >= fromUtc);
        if (driverId.HasValue)
        {
            source = source.Where(item => item.DriverId == driverId.Value);
        }

        if (vehicleId.HasValue)
        {
            source = source.Where(item => item.VehicleId == vehicleId.Value);
        }

        var count = await source.CountAsync(cancellationToken);
        if (count > 2000)
        {
            throw new ValidationAppException("تعداد مأموریت‌های تایم‌لاین بیش از ۲۰۰۰ است؛ بازه را محدود کنید.");
        }

        return await source
            .OrderBy(item => item.PlannedStartAtUtc)
            .Select(item => new TimelineMissionDto(
                item.Id,
                item.MissionNumber,
                item.Title,
                item.Status,
                item.Type,
                item.PlannedStartAtUtc,
                item.PlannedEndAtUtc,
                item.DriverId,
                db.Drivers.Where(d => d.Id == item.DriverId).Select(d => d.FirstName + " " + d.LastName).FirstOrDefault(),
                item.VehicleId,
                db.Vehicles.Where(v => v.Id == item.VehicleId).Select(v => v.PlateDisplay).FirstOrDefault(),
                item.RequesterName,
                item.Origin,
                item.Destination))
            .ToListAsync(cancellationToken);
    }
}

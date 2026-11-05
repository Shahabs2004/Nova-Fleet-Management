using NovaFleet.Domain.Missions;

namespace NovaFleet.Application.Timeline;

public sealed record TimelineMissionDto(
    Guid Id,
    string MissionNumber,
    string Title,
    MissionStatus Status,
    MissionType Type,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    Guid? DriverId,
    string? DriverName,
    Guid? VehicleId,
    string? VehiclePlate,
    string RequesterName,
    string Origin,
    string Destination);

public interface ITimelineService
{
    /// <summary>مأموریت‌های مؤثر در بازه؛ لغوشده‌ها حذف و در انتظار انصراف با وضعیت خود نمایش داده می‌شوند.</summary>
    Task<IReadOnlyList<TimelineMissionDto>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? driverId,
        Guid? vehicleId,
        CancellationToken cancellationToken = default);
}

using NovaFleet.Application.Missions;

namespace NovaFleet.Application.Dashboard;

public sealed record DashboardChartPointDto(string Label, double Value);

public sealed record DashboardWorkloadDto(string Name, double Value);

public sealed record DashboardStatsDto(
    int PendingRequests,
    int ActiveMissions,
    int ReadyVehicles,
    int ActiveDrivers,
    int PendingWithdrawals,
    int PendingChangeRequests,
    int FailedSms,
    int MissionsThisMonth,
    long DistanceThisMonthKm,
    IReadOnlyList<DashboardChartPointDto> MissionsByDay,
    IReadOnlyList<DashboardWorkloadDto> DriverWorkload,
    IReadOnlyList<DashboardChartPointDto> VehicleStatuses,
    IReadOnlyList<MissionListItemDto> UpcomingMissions);

public interface IDashboardService
{
    /// <summary>کارت‌ها فقط در محدوده مجوز کاربر پر می‌شوند؛ بقیه مقدار صفر یا خالی دارند.</summary>
    Task<DashboardStatsDto> GetAsync(CancellationToken cancellationToken = default);
}

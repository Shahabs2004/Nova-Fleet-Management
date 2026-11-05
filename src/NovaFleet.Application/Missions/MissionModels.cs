using NovaFleet.Application.Common;
using NovaFleet.Domain.Missions;

namespace NovaFleet.Application.Missions;

public sealed record MissionQuery(
    string? Search,
    IReadOnlyList<MissionStatus>? Statuses,
    MissionType? Type,
    Guid? DriverId,
    Guid? VehicleId,
    string? OrganizationalUnit,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    bool OnlyMine,
    int Page,
    int PageSize);

public sealed record MissionListItemDto(
    Guid Id,
    string MissionNumber,
    string Title,
    MissionType Type,
    MissionStatus Status,
    string RequesterName,
    string RequesterOrganizationalUnit,
    string Origin,
    string Destination,
    DateTimeOffset PlannedStartAtUtc,
    DateTimeOffset PlannedEndAtUtc,
    DateTimeOffset? ActualEndAtUtc,
    Guid? DriverId,
    string? DriverName,
    Guid? VehicleId,
    string? VehiclePlate,
    string? SplitReason,
    Guid? RelatedMissionId,
    bool IsOwn);

public sealed record MissionRequestInput(
    string Title,
    MissionType Type,
    string Purpose,
    string Origin,
    IReadOnlyList<string> IntermediateStops,
    string Destination,
    DateTimeOffset PlannedStartAtUtc,
    DateTimeOffset PlannedEndAtUtc,
    string RequestingGroup,
    string RequesterPhoneSnapshot,
    string? RequesterOrganizationalUnit,
    string? Description,
    int? PassengerCount,
    string? MissionOrderNumber,
    DateOnly? MissionOrderDate,
    string? CargoDescription,
    string? ConsigneeName,
    string? PassengerNames);

public sealed record MissionChangeInput(
    string Title,
    MissionType Type,
    string Purpose,
    string Origin,
    IReadOnlyList<string> IntermediateStops,
    string Destination,
    DateTimeOffset PlannedStartAtUtc,
    DateTimeOffset PlannedEndAtUtc,
    string RequestingGroup,
    string RequesterPhoneSnapshot,
    string? Description,
    int? PassengerCount,
    string? MissionOrderNumber,
    DateOnly? MissionOrderDate,
    string? CargoDescription,
    string? ConsigneeName,
    string? PassengerNames,
    Guid? DriverId,
    Guid? VehicleId,
    long? OdometerStartKm);

public sealed record MissionChangeRequestDto(
    Guid Id,
    string RequesterUsername,
    string Reason,
    MissionChangeRequestStatus Status,
    string? ReviewerUsername,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewStartedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    string? DecisionReason,
    MissionChangeInput Proposed);

public sealed record MissionDetailsDto(
    Guid Id,
    string MissionNumber,
    string Title,
    MissionType Type,
    MissionStatus Status,
    string RequesterUsername,
    string RequesterName,
    string RequesterPhoneSnapshot,
    string RequesterOrganizationalUnit,
    string RequestingGroup,
    string Purpose,
    string Origin,
    IReadOnlyList<string> IntermediateStops,
    string Destination,
    DateTimeOffset PlannedStartAtUtc,
    DateTimeOffset PlannedEndAtUtc,
    DateTimeOffset? ActualStartAtUtc,
    DateTimeOffset? ActualEndAtUtc,
    Guid? DriverId,
    string? DriverName,
    string? DriverMobile,
    Guid? VehicleId,
    string? VehicleDisplayName,
    string? VehiclePlate,
    string? Description,
    int? PassengerCount,
    string? MissionOrderNumber,
    DateOnly? MissionOrderDate,
    string? CargoDescription,
    string? ConsigneeName,
    string? PassengerNames,
    long? OdometerStartKm,
    long? OdometerEndKm,
    long? DistanceKm,
    string? DecisionReason,
    MissionStatus? StatusBeforeWithdrawal,
    Guid? RelatedMissionId,
    string? RelatedMissionNumber,
    string? SplitReason,
    DateTimeOffset? SplitAtUtc,
    string? SplitBy,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? ReviewStartedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string RowVersion,
    bool IsOwn,
    IReadOnlyList<MissionChangeRequestDto> ChangeRequests);

public sealed record MissionApprovalRequest(Guid DriverId, Guid VehicleId, string? Note);

public sealed record MissionRejectionRequest(string Reason);

public sealed record MissionCancelRequest(string Reason);

public sealed record MissionExtensionRequest(DateTimeOffset NewPlannedEndAtUtc, string Reason);

public sealed record MissionEndRequest(DateTimeOffset EndedAtUtc, long? OdometerEndKm, string Reason);

public sealed record MissionWithdrawalRequest(string Reason);

public sealed record MissionWithdrawalDecision(string? Reason);

public sealed record MissionSplitRequest(DateTimeOffset SplitAtUtc, long OdometerEndKm, Guid? SecondDriverId, Guid? SecondVehicleId, string Reason, string? Note);

public sealed record MissionChangeRequestInput(string Reason, MissionChangeInput Proposed);

public sealed record MissionChangeDecision(string? Reason);

public sealed record MissionOdometerRequest(long? StartKm, long? EndKm, string? RowVersion);

public sealed record MissionAssignmentDto(Guid DriverId, Guid VehicleId);

public sealed record MissionAvailabilityDto(
    bool HasConflict,
    string? Message,
    string? DriverName,
    string? VehiclePlate);

/// <summary>سناریوهای ثبت، بررسی، تخصیص، تغییر، انصراف، تقسیم و کیلومتر مأموریت.</summary>
public interface IMissionService
{
    Task<PagedResult<MissionListItemDto>> SearchAsync(MissionQuery query, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> CreateAsync(MissionRequestInput input, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> UpdatePendingAsync(Guid id, MissionRequestInput input, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> StartReviewAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> ApproveAsync(Guid id, MissionApprovalRequest request, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> RejectAsync(Guid id, MissionRejectionRequest request, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> CancelAsync(Guid id, MissionCancelRequest request, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> ExtendAsync(Guid id, MissionExtensionRequest request, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> EndAsync(Guid id, MissionEndRequest request, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> SetOdometerAsync(Guid id, MissionOdometerRequest request, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> RequestWithdrawalAsync(Guid id, MissionWithdrawalRequest request, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> DecideWithdrawalAsync(Guid id, bool approve, MissionWithdrawalDecision decision, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> RequestChangeAsync(Guid id, MissionChangeRequestInput request, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> ReviewChangeAsync(Guid id, Guid changeRequestId, bool start, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> DecideChangeAsync(Guid id, Guid changeRequestId, bool approve, MissionChangeDecision decision, CancellationToken cancellationToken = default);

    Task<MissionDetailsDto> SplitAsync(Guid id, MissionSplitRequest request, CancellationToken cancellationToken = default);

    /// <summary>بررسی تداخل زمانی و وضعیت منابع پیش از تأیید یا تغییر.</summary>
    Task<MissionAvailabilityDto> CheckAvailabilityAsync(Guid missionId, Guid driverId, Guid vehicleId, DateTimeOffset startAtUtc, DateTimeOffset endAtUtc, CancellationToken cancellationToken = default);

    /// <summary>سند حکم مأموریت برای چاپ و بایگانی.</summary>
    Task<byte[]> ExportOrderPdfAsync(Guid id, CancellationToken cancellationToken = default);
}

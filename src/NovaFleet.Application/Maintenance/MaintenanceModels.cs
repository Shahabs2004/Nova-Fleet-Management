using NovaFleet.Application.Common;

namespace NovaFleet.Application.Maintenance;

public sealed record TemplateInput(string Name, string Category, string AssetKey, long? IntervalKm,
    int? IntervalMonths, int WarningKm, int WarningDays, bool IsActive, int Version);
public sealed record TemplateDto(Guid Id, string Name, string Category, string AssetKey, long? IntervalKm,
    int? IntervalMonths, int WarningKm, int WarningDays, bool IsActive, int Version);
public sealed record PartInput(string Name, string? Specification, int Quantity, decimal UnitCostToman, DateOnly? WarrantyUntil);
public sealed record ItemInput(Guid TemplateId, Guid? ScheduleId, decimal LaborCostToman, IReadOnlyList<PartInput> Parts);
public sealed record RecordInput(Guid RequestId, DateTimeOffset PerformedAtUtc, long? OdometerKm,
    string? Workshop, string? Notes, decimal OtherCostToman, IReadOnlyList<ItemInput> Items, int Version, string? Reason);
public sealed record PartDto(Guid Id, string Name, string? Specification, int Quantity, decimal UnitCostToman, DateOnly? WarrantyUntil);
public sealed record ItemDto(Guid Id, Guid TemplateId, Guid? ScheduleId, string Name, string Category,
    string AssetKey, decimal LaborCostToman, IReadOnlyList<PartDto> Parts);
public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, int Size, string UploadedBy, DateTimeOffset CreatedAtUtc);
public sealed record RecordDto(Guid Id, Guid VehicleId, DateTimeOffset PerformedAtUtc, long? OdometerKm,
    string? Workshop, string? Notes, decimal OtherCostToman, decimal TotalCostToman, bool IsVoided,
    string CreatedBy, int Version, IReadOnlyList<ItemDto> Items, IReadOnlyList<AttachmentDto> Attachments);
public sealed record RevisionDto(Guid Id, string EntityKind, Guid EntityId, int Version, string BeforeJson,
    string Reason, string Actor, DateTimeOffset CreatedAtUtc);
public sealed record RevisionInput(int Version, string Reason);
public sealed record ScheduleInput(Guid TemplateId, string Name, long? IntervalKm, int? IntervalMonths,
    int WarningKm, int WarningDays, long? BaselineKm, DateTimeOffset? BaselineAtUtc, bool IsActive, int Version);
public sealed record ScheduleDto(Guid Id, Guid TemplateId, string Name, string AssetKey,
    long? IntervalKm, int? IntervalMonths, int WarningKm, int WarningDays,
    long? BaselineKm, DateTimeOffset? BaselineAtUtc, bool IsActive, int Version,
    long? LastKm, DateTimeOffset? LastAtUtc, long? DueKm, DateTimeOffset? DueAtUtc, string State, string CycleKey);
public sealed record OdometerInput(Guid RequestId, DateTimeOffset ObservedAtUtc, long Kilometers, int Version, string? Reason);
public sealed record OdometerDto(Guid Id, string SourceKind, Guid SourceId, DateTimeOffset ObservedAtUtc,
    bool IsTimeEstimated, long Kilometers, bool IsVoided, string RecordedBy, int Version, bool HasConflict);
public sealed record OdometerSummary(long? CurrentKm, DateTimeOffset? ObservedAtUtc, bool IsTimeEstimated, bool HasConflicts, IReadOnlyList<OdometerDto> Readings);
public sealed record MaintenanceVehicleDto(Guid VehicleId, string DisplayName, string Plate, bool IsArchived,
    long? CurrentKm, DateTimeOffset? OdometerAtUtc, bool OdometerTimeEstimated, bool HasOdometerConflicts,
    decimal TotalCostToman, DateTimeOffset? LastServiceAtUtc, int DueCount, int ApproachingCount, IReadOnlyList<ScheduleDto> Schedules);
public sealed record ExpensePoint(string Period, decimal CostToman);
public sealed record CategoryExpense(string Category, decimal CostToman);
public sealed record MaintenanceReport(DateTimeOffset FromUtc, DateTimeOffset ToUtc, decimal TotalCostToman,
    long? DistanceKm, decimal? CostPerKmToman, DateTimeOffset? DistanceFromUtc, DateTimeOffset? DistanceToUtc,
    decimal? DistanceCostToman, IReadOnlyList<ExpensePoint> Months, IReadOnlyList<CategoryExpense> Categories);
public sealed record AttachmentDownload(string FileName, string ContentType, byte[] Content);

public interface IMaintenanceService
{
    Task<IReadOnlyList<TemplateDto>> TemplatesAsync(CancellationToken ct);
    Task<TemplateDto> SaveTemplateAsync(Guid? id, TemplateInput input, CancellationToken ct);
    Task<PagedResult<MaintenanceVehicleDto>> FleetAsync(string? search, string? state, int page, int pageSize, CancellationToken ct);
    Task<MaintenanceVehicleDto> VehicleAsync(Guid vehicleId, CancellationToken ct);
    Task<PagedResult<RecordDto>> RecordsAsync(Guid vehicleId, int page, int pageSize, bool includeVoided, CancellationToken ct);
    Task<RecordDto> RecordAsync(Guid vehicleId, Guid id, CancellationToken ct);
    Task<RecordDto> SaveRecordAsync(Guid vehicleId, Guid? id, RecordInput input, CancellationToken ct);
    Task VoidRecordAsync(Guid vehicleId, Guid id, RevisionInput input, CancellationToken ct);
    Task<IReadOnlyList<RevisionDto>> RevisionsAsync(Guid vehicleId, Guid entityId, CancellationToken ct);
    Task<ScheduleDto> SaveScheduleAsync(Guid vehicleId, Guid? id, ScheduleInput input, CancellationToken ct);
    Task<OdometerSummary> OdometerAsync(Guid vehicleId, CancellationToken ct);
    Task<OdometerDto> SaveOdometerAsync(Guid vehicleId, Guid? id, OdometerInput input, CancellationToken ct);
    Task<MaintenanceReport> ReportAsync(Guid vehicleId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<AttachmentDto> AddAttachmentAsync(Guid vehicleId, Guid recordId, string name, byte[] content, CancellationToken ct);
    Task<AttachmentDownload> DownloadAsync(Guid vehicleId, Guid recordId, Guid attachmentId, CancellationToken ct);
}

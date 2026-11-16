using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Maintenance;

public sealed class MaintenanceTemplate : Entity
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string AssetKey { get; set; } = "general-service";
    public long? IntervalKm { get; set; }
    public int? IntervalMonths { get; set; }
    public int WarningKm { get; set; } = 1000;
    public int WarningDays { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

public sealed class MaintenanceRecord : Entity
{
    public Guid VehicleId { get; set; }
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public DateTimeOffset PerformedAtUtc { get; set; }
    public string? Workshop { get; set; }
    public string? Notes { get; set; }
    public decimal OtherCostToman { get; set; }
    public bool IsVoided { get; set; }
    public string CreatedBy { get; set; } = "";
    public int Version { get; set; } = 1;
    public List<MaintenanceRecordItem> Items { get; set; } = [];
}

public sealed class MaintenanceRecordItem : Entity
{
    public Guid RecordId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid? ScheduleId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string AssetKey { get; set; } = "general-service";
    public decimal LaborCostToman { get; set; }
    public List<MaintenanceRecordPart> Parts { get; set; } = [];
}

public sealed class MaintenanceRecordPart : Entity
{
    public Guid ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? Specification { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCostToman { get; set; }
    public DateOnly? WarrantyUntil { get; set; }
}

public sealed class MaintenanceSchedule : Entity
{
    public Guid VehicleId { get; set; }
    public Guid TemplateId { get; set; }
    public string Name { get; set; } = "";
    public long? IntervalKm { get; set; }
    public int? IntervalMonths { get; set; }
    public int WarningKm { get; set; } = 1000;
    public int WarningDays { get; set; } = 30;
    public long? BaselineKm { get; set; }
    public DateTimeOffset? BaselineAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

public sealed class VehicleOdometerReading : Entity
{
    public Guid VehicleId { get; set; }
    public string SourceKind { get; set; } = "manual";
    public Guid SourceId { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public bool IsTimeEstimated { get; set; }
    public long Kilometers { get; set; }
    public bool IsVoided { get; set; }
    public string RecordedBy { get; set; } = "";
    public int Version { get; set; } = 1;
}

public sealed class MaintenanceRevision : Entity
{
    public string EntityKind { get; set; } = "";
    public Guid EntityId { get; set; }
    public Guid VehicleId { get; set; }
    public int Version { get; set; }
    public string BeforeJson { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
}

public sealed class MaintenanceAttachment : Entity
{
    public Guid RecordId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string UploadedBy { get; set; } = "";
}

public sealed class MaintenanceReminderDelivery : Entity
{
    public Guid ScheduleId { get; set; }
    public string CycleKey { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Recipient { get; set; } = "";
}

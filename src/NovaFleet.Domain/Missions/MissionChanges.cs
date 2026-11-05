namespace NovaFleet.Domain.Missions;

/// <summary>مقادیر قابل ویرایش درخواست تا پیش از شروع بررسی.</summary>
public sealed record MissionRequestChanges(
    string Title,
    string RequesterPhoneSnapshot,
    string RequestingGroup,
    MissionType Type,
    string Purpose,
    string Origin,
    string Destination,
    IReadOnlyList<string> IntermediateStops,
    DateTimeOffset PlannedStartAtUtc,
    DateTimeOffset PlannedEndAtUtc,
    string? Description,
    int? PassengerCount,
    string? MissionOrderNumber,
    DateOnly? MissionOrderDate,
    string? CargoDescription,
    string? ConsigneeName,
    string? PassengerNames);

/// <summary>مقادیر پیشنهادی برای اعمال اتمی پس از تأیید درخواست تغییر.</summary>
public sealed record MissionChanges(
    string Title,
    MissionType Type,
    string Purpose,
    string Origin,
    string Destination,
    IReadOnlyList<string> IntermediateStops,
    DateTimeOffset PlannedStartAtUtc,
    DateTimeOffset PlannedEndAtUtc,
    Guid DriverId,
    Guid VehicleId,
    string RequesterName,
    string RequesterPhoneSnapshot,
    string RequesterOrganizationalUnit,
    string RequestingGroup,
    string? Description,
    int? PassengerCount,
    string? MissionOrderNumber,
    DateOnly? MissionOrderDate,
    string? CargoDescription,
    string? ConsigneeName,
    string? PassengerNames,
    long? OdometerStartKm);

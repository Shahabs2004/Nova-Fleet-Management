using NovaFleet.Domain.Missions;

namespace NovaFleet.Application.Reports;

public sealed record ReportDefinitionDto(string Key, string Title, string Description);

public sealed record ReportColumnDto(string Key, string Title, bool IsNumeric = false);

/// <summary>فیلتر مشترک هشت گزارش؛ هر گزارش فقط فیلترهای معتبر خود را اعمال می‌کند.</summary>
public sealed record ReportQuery(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? DriverId,
    Guid? VehicleId,
    MissionType? Type,
    string? OrganizationalUnit,
    MissionStatus? Status);

public sealed record ReportTableDto(
    string Key,
    string Title,
    IReadOnlyList<ReportColumnDto> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyDictionary<string, object?> Totals,
    DateTimeOffset GeneratedAtUtc,
    string? Subtitle = null);

/// <summary>هشت گزارش مصوب SPEC؛ خروجی Excel و PDF فقط با مجوز صادر می‌شود.</summary>
public interface IReportService
{
    IReadOnlyList<ReportDefinitionDto> GetDefinitions();

    Task<ReportTableDto> RunAsync(string key, ReportQuery query, CancellationToken cancellationToken = default);

    Task<byte[]> ExportExcelAsync(string key, ReportQuery query, CancellationToken cancellationToken = default);

    Task<byte[]> ExportPdfAsync(string key, ReportQuery query, CancellationToken cancellationToken = default);
}

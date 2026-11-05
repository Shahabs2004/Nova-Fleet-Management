using NovaFleet.Domain.Locations;

namespace NovaFleet.Application.Locations;

public sealed record LocationDto(
    Guid Id,
    string Name,
    LocationStatus Status,
    string ProposedBy,
    string? ReviewedBy,
    string? ReviewReason,
    DateTimeOffset? ReviewedAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record ProposedLocationDto(Guid Id, string Name, DateTimeOffset CreatedAtUtc);

public interface ILocationService
{
    Task<IReadOnlyList<LocationDto>> ListAsync(LocationStatus? status, string? search, CancellationToken cancellationToken = default);

    /// <summary>مکان‌های تأییدشده برای تکمیل خودکار فرم مأموریت.</summary>
    Task<IReadOnlyList<string>> ListApprovedNamesAsync(CancellationToken cancellationToken = default);

    Task<LocationDto> ProposeAsync(string name, CancellationToken cancellationToken = default);

    Task<LocationDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LocationDto> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    Task<LocationDto> DisableAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    Task<LocationDto> ReopenAsync(Guid id, string name, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

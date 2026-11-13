using NovaFleet.Application.Common;
using NovaFleet.Domain.Drivers;

namespace NovaFleet.Application.Drivers;

public sealed record DriverDto(
    Guid Id,
    string FirstName,
    string LastName,
    string DisplayName,
    string MobilePhone,
    DriverEmploymentStatus EmploymentStatus,
    Guid? DefaultVehicleId,
    string? DefaultVehiclePlate,
    bool IsArchived,
    DateTimeOffset CreatedAtUtc,
    string RowVersion);

public sealed record DriverOptionDto(Guid Id, string DisplayName, string MobilePhone, Guid? DefaultVehicleId);

public sealed record DriverCreateRequest(string FirstName, string LastName, string MobilePhone);

public sealed record DriverUpdateRequest(string FirstName, string LastName, string MobilePhone, string? RowVersion);

public interface IDriverService
{
    Task<PagedResult<DriverDto>> SearchAsync(
        string? search,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DriverOptionDto>> ListOptionsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<DriverDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DriverDto> CreateAsync(DriverCreateRequest request, CancellationToken cancellationToken = default);

    Task<DriverDto> UpdateAsync(Guid id, DriverUpdateRequest request, CancellationToken cancellationToken = default);

    Task<DriverDto> SetEmploymentStatusAsync(Guid id, DriverEmploymentStatus status, string? rowVersion, CancellationToken cancellationToken = default);

    Task<DriverDto> SetDefaultVehicleAsync(Guid id, Guid? vehicleId, string? rowVersion, CancellationToken cancellationToken = default);
    Task SetVehicleDriverAsync(Guid vehicleId, Guid? driverId, string? rowVersion, CancellationToken cancellationToken = default);

    Task<DriverDto> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
}

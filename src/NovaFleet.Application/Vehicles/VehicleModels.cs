using NovaFleet.Application.Common;
using NovaFleet.Domain.Vehicles;

namespace NovaFleet.Application.Vehicles;

public sealed record VehicleDto(
    Guid Id,
    string Brand,
    string Model,
    string DisplayName,
    string LicensePlate,
    string PlateDisplay,
    string PlateFirstTwo,
    string PlateLetter,
    string PlateNextThree,
    string PlateIranCode,
    ManualVehicleStatus ManualStatus,
    VehicleEffectiveStatus EffectiveStatus,
    bool IsArchived,
    DateTimeOffset CreatedAtUtc,
    string RowVersion);

public sealed record VehicleOptionDto(
    Guid Id,
    string DisplayName,
    string PlateDisplay,
    VehicleEffectiveStatus EffectiveStatus,
    ManualVehicleStatus ManualStatus);

public sealed record VehicleCreateRequest(
    string Brand,
    string Model,
    string PlateFirstTwo,
    string PlateLetter,
    string PlateNextThree,
    string PlateIranCode);

public sealed record VehicleUpdateRequest(
    string Brand,
    string Model,
    string PlateFirstTwo,
    string PlateLetter,
    string PlateNextThree,
    string PlateIranCode,
    string? RowVersion);

public interface IVehicleService
{
    Task<PagedResult<VehicleDto>> SearchAsync(
        string? search,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VehicleOptionDto>> ListOptionsAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<VehicleDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<VehicleDto> CreateAsync(VehicleCreateRequest request, CancellationToken cancellationToken = default);

    Task<VehicleDto> UpdateAsync(Guid id, VehicleUpdateRequest request, CancellationToken cancellationToken = default);

    Task<VehicleDto> SetManualStatusAsync(Guid id, ManualVehicleStatus status, string? rowVersion, CancellationToken cancellationToken = default);

    Task<VehicleDto> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
}

namespace NovaFleet.Application.Organization;

public sealed record OrganizationUnitDto(Guid Id, string Name, bool IsActive);

public interface IOrganizationUnitService
{
    Task<IReadOnlyList<OrganizationUnitDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<OrganizationUnitDto> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<OrganizationUnitDto> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default);

    Task<OrganizationUnitDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default);
}

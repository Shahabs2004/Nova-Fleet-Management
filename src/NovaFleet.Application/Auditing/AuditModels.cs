using NovaFleet.Application.Common;

namespace NovaFleet.Application.Auditing;

public sealed record AuditEntryDto(
    Guid Id,
    string ActorUsername,
    string Action,
    string EntityType,
    string? EntityId,
    string Outcome,
    string? Reason,
    string? BeforeJson,
    string? AfterJson,
    string? ClientIpAddress,
    DateTimeOffset OccurredAtUtc);

public sealed record AuditQuery(
    string? Actor,
    string? Action,
    string? EntityType,
    string? Outcome,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page,
    int PageSize);

public interface IAuditQueryService
{
    Task<PagedResult<AuditEntryDto>> ListAsync(AuditQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken = default);
}

using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Auditing;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Auditing;

/// <summary>کاوش سوابق ممیزی؛ فقط خواندنی و بدون امکان ویرایش یا حذف از رابط کاربری.</summary>
public sealed class AuditQueryService(NovaFleetDbContext db, ICurrentUser currentUser) : IAuditQueryService
{
    public async Task<PagedResult<AuditEntryDto>> ListAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Security.AuditView);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var source = db.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            var actor = query.Actor.Trim();
            source = source.Where(item => item.ActorUsername.Contains(actor));
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            source = source.Where(item => item.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            source = source.Where(item => item.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(query.Outcome))
        {
            source = source.Where(item => item.Outcome == query.Outcome);
        }

        if (query.FromUtc.HasValue)
        {
            source = source.Where(item => item.OccurredAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            source = source.Where(item => item.OccurredAtUtc <= query.ToUtc.Value);
        }

        var total = await source.CountAsync(cancellationToken);
        var items = await source
            .OrderByDescending(item => item.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new AuditEntryDto(
                item.Id,
                item.ActorUsername,
                item.Action,
                item.EntityType,
                item.EntityId,
                item.Outcome,
                item.Reason,
                item.BeforeJson,
                item.AfterJson,
                item.ClientIpAddress,
                item.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntryDto>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Security.AuditView);
        return await db.AuditEntries.AsNoTracking()
            .Select(item => item.Action)
            .Distinct()
            .OrderBy(item => item)
            .ToListAsync(cancellationToken);
    }
}

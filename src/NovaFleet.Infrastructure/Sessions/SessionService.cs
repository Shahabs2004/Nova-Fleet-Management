using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Sessions;
using NovaFleet.Domain.Sessions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Sessions;

/// <summary>سوابق ورود/خروج و اعتبارسنجی نشست فعال با سیاست عمر مطلق و عدم فعالیت.</summary>
public sealed class SessionService(NovaFleetDbContext db, IClock clock) : ISessionService
{
    public async Task<PagedResult<UserSessionDto>> ListAsync(
        SessionQuery query,
        CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var source =
            from session in db.UserSessions.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.Username equals user.Username into users
            from user in users.DefaultIfEmpty()
            select new { session, DisplayName = user == null ? session.Username : user.FirstName + " " + user.LastName };

        if (!string.IsNullOrWhiteSpace(query.Username))
        {
            var username = query.Username.Trim();
            source = source.Where(item => item.session.Username.Contains(username));
        }

        if (query.Status.HasValue)
        {
            source = source.Where(item => item.session.Status == query.Status.Value);
        }

        if (query.FromUtc.HasValue)
        {
            source = source.Where(item => item.session.LoginAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            source = source.Where(item => item.session.LoginAtUtc <= query.ToUtc.Value);
        }

        var total = await source.CountAsync(cancellationToken);
        var items = await source
            .OrderByDescending(item => item.session.LoginAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new UserSessionDto(
                item.session.Id,
                item.session.Username,
                item.DisplayName,
                item.session.LoginMethod,
                item.session.Status,
                item.session.LoginAtUtc,
                item.session.LastActivityAtUtc,
                item.session.EndedAtUtc,
                item.session.ClientIpAddress,
                item.session.Reason))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserSessionDto>(items, total, page, pageSize);
    }

    public async Task<bool> ValidateAsync(
        string sessionKey,
        int absoluteMinutes,
        int idleMinutes,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sessionKey, out var key))
        {
            return false;
        }

        var now = clock.UtcNow;
        var session = await db.UserSessions.FirstOrDefaultAsync(item => item.SessionKey == key, cancellationToken);
        if (session is null || session.Status != UserSessionStatus.Active)
        {
            return false;
        }

        if (now >= session.LoginAtUtc.AddMinutes(absoluteMinutes))
        {
            session.Expire(now, "پایان عمر مطلق نشست.");
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (now >= session.LastActivityAtUtc.AddMinutes(idleMinutes))
        {
            session.Expire(now, "گذشت زمان عدم فعالیت.");
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (now - session.LastActivityAtUtc >= TimeSpan.FromMinutes(1))
        {
            session.Touch(now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task ForceEndAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundAppException("نشست پیدا نشد.");

        session.End(clock.UtcNow, "پایان اجباری توسط مدیر.");
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> ActiveCountAsync(CancellationToken cancellationToken = default)
        => db.UserSessions.CountAsync(item => item.Status == UserSessionStatus.Active, cancellationToken);
}

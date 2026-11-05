using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Notifications;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Notifications;

/// <summary>اعلان‌های داخلی دائمی؛ هر کاربر فقط اعلان‌های خود را می‌بیند.</summary>
public sealed class NotificationService(NovaFleetDbContext db, ICurrentUser currentUser) : INotificationService
{
    public async Task<NotificationPageDto> ListAsync(
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);
        var username = RequireUsername();
        var source = db.UserNotifications.AsNoTracking().Where(item => item.RecipientUsername == username);
        if (unreadOnly)
        {
            source = source.Where(item => item.ReadAtUtc == null);
        }

        var total = await source.CountAsync(cancellationToken);
        var unread = await db.UserNotifications.CountAsync(
            item => item.RecipientUsername == username && item.ReadAtUtc == null,
            cancellationToken);
        var items = await source
            .OrderByDescending(item => item.CreatedAtUtc)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .Select(item => new NotificationDto(
                item.Id,
                item.Title,
                item.Message,
                item.EventKey,
                item.ReferenceType,
                item.ReferenceId,
                item.CreatedAtUtc,
                item.ReadAtUtc))
            .ToListAsync(cancellationToken);

        return new NotificationPageDto(items, unread, total);
    }

    public async Task<IReadOnlyList<NotificationDto>> ListRecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var username = RequireUsername();
        return await db.UserNotifications.AsNoTracking()
            .Where(item => item.RecipientUsername == username)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(Math.Clamp(count, 1, 50))
            .Select(item => new NotificationDto(
                item.Id,
                item.Title,
                item.Message,
                item.EventKey,
                item.ReferenceType,
                item.ReferenceId,
                item.CreatedAtUtc,
                item.ReadAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var username = RequireUsername();
        return db.UserNotifications.CountAsync(
            item => item.RecipientUsername == username && item.ReadAtUtc == null,
            cancellationToken);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var username = RequireUsername();
        var notification = await db.UserNotifications
            .FirstOrDefaultAsync(item => item.Id == id && item.RecipientUsername == username, cancellationToken)
            ?? throw new NotFoundAppException("اعلان پیدا نشد.");

        notification.MarkRead(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var username = RequireUsername();
        var now = DateTimeOffset.UtcNow;
        var items = await db.UserNotifications
            .Where(item => item.RecipientUsername == username && item.ReadAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.MarkRead(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private string RequireUsername() =>
        currentUser.IsAuthenticated
            ? currentUser.Username
            : throw new UnauthorizedAppException("برای مشاهده اعلان‌ها باید وارد شوید.");
}

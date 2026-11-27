using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Notifications;

/// <summary>ثبت اعلان داخلی برای گیرندگان مجاز؛ گیرنده غیرفعال یا ناموجود نادیده گرفته می‌شود.</summary>
public sealed class NotificationPublisher(
    NovaFleetDbContext db,
    IPermissionResolver permissionResolver) : INotificationPublisher
{
    public async Task PublishAsync(
        NotificationMessage message,
        IEnumerable<string> usernames,
        CancellationToken cancellationToken = default)
    {
        var requested = usernames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
        {
            return;
        }

        var recipients = await db.Users.AsNoTracking()
            .Where(item => requested.Contains(item.Username) && !item.IsDisabled)
            .Select(item => item.Username)
            .ToListAsync(cancellationToken);

        await SaveAsync(message, recipients, cancellationToken);
    }

    public async Task PublishToPermissionAsync(
        NotificationMessage message,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var recipients = await permissionResolver.ListUsernamesWithPermissionAsync(permission, cancellationToken);
        await SaveAsync(message, recipients, cancellationToken);
    }

    private async Task SaveAsync(
        NotificationMessage message,
        IReadOnlyList<string> recipients,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        var entities = recipients
            .Select(username => new UserNotification(
                username,
                message.Title,
                message.Message,
                message.EventKey,
                message.ReferenceType,
                message.ReferenceId))
            .ToList();

        db.UserNotifications.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
    }
}

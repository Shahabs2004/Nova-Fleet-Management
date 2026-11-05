using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Notifications;

/// <summary>ثبت اعلان برای گیرندگان مجاز و ارسال زنده؛ گیرنده غیرفعال یا ناموجود نادیده گرفته می‌شود.</summary>
public sealed class NotificationPublisher(
    NovaFleetDbContext db,
    IPermissionResolver permissionResolver,
    IRealtimeNotifier realtime) : INotificationPublisher
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

        await SaveAndNotifyAsync(message, recipients, cancellationToken);
    }

    public async Task PublishToPermissionAsync(
        NotificationMessage message,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var recipients = await permissionResolver.ListUsernamesWithPermissionAsync(permission, cancellationToken);
        await SaveAndNotifyAsync(message, recipients, cancellationToken);
    }

    public async Task PublishToRolesAsync(
        NotificationMessage message,
        IReadOnlyList<string> roleNames,
        CancellationToken cancellationToken = default)
    {
        if (roleNames.Count == 0)
        {
            return;
        }

        var recipients = await (
                from userRole in db.UserRoles.AsNoTracking()
                join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                join user in db.Users.AsNoTracking() on userRole.UserId equals user.Id
                where roleNames.Contains(role.Name) && role.IsActive && !user.IsDisabled
                select user.Username)
            .Distinct()
            .ToListAsync(cancellationToken);

        await SaveAndNotifyAsync(message, recipients, cancellationToken);
    }

    private async Task SaveAndNotifyAsync(
        NotificationMessage message,
        IReadOnlyList<string> recipients,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
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

        foreach (var entity in entities)
        {
            var realtimeMessage = new RealtimeMessage(
                entity.Id,
                entity.Title,
                entity.Message,
                entity.EventKey,
                entity.ReferenceType,
                entity.ReferenceId,
                now);

            await realtime.NotifyUsersAsync([entity.RecipientUsername], realtimeMessage, cancellationToken);
        }
    }
}

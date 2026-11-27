namespace NovaFleet.Application.Abstractions;

public sealed record NotificationMessage(
    string Title,
    string Message,
    string EventKey,
    string? ReferenceType = null,
    string? ReferenceId = null);

/// <summary>ثبت اعلان داخلی برای کاربران هدف.</summary>
public interface INotificationPublisher
{
    Task PublishAsync(NotificationMessage message, IEnumerable<string> usernames, CancellationToken cancellationToken = default);

    /// <summary>ارسال به همه کاربران فعالی که مجوز مشخص‌شده را دارند.</summary>
    Task PublishToPermissionAsync(NotificationMessage message, string permission, CancellationToken cancellationToken = default);
}

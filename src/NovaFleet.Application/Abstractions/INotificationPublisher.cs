namespace NovaFleet.Application.Abstractions;

public sealed record NotificationMessage(
    string Title,
    string Message,
    string EventKey,
    string? ReferenceType = null,
    string? ReferenceId = null);

/// <summary>ثبت اعلان داخلی برای کاربران هدف و ارسال زنده آن.</summary>
public interface INotificationPublisher
{
    Task PublishAsync(NotificationMessage message, IEnumerable<string> usernames, CancellationToken cancellationToken = default);

    /// <summary>ارسال به همه کاربران فعالی که مجوز مشخص‌شده را دارند.</summary>
    Task PublishToPermissionAsync(NotificationMessage message, string permission, CancellationToken cancellationToken = default);

    Task PublishToRolesAsync(NotificationMessage message, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default);
}

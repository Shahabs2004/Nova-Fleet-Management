using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Notifications;

/// <summary>اعلان داخلی یک کاربر؛ تاریخچه دائمی و قابل علامت‌گذاری خوانده‌شده.</summary>
public sealed class UserNotification : Entity
{
    private UserNotification()
    {
    }

    public string RecipientUsername { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public string EventKey { get; private set; } = string.Empty;

    public string? ReferenceType { get; private set; }

    public string? ReferenceId { get; private set; }

    public DateTimeOffset? ReadAtUtc { get; private set; }

    public UserNotification(
        string recipientUsername,
        string title,
        string message,
        string eventKey,
        string? referenceType = null,
        string? referenceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientUsername, nameof(recipientUsername));
        ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));
        RecipientUsername = recipientUsername.Trim();
        Title = title.Trim();
        Message = message.Trim();
        EventKey = string.IsNullOrWhiteSpace(eventKey) ? "system" : eventKey.Trim();
        ReferenceType = referenceType;
        ReferenceId = referenceId;
    }

    public void MarkRead(DateTimeOffset nowUtc) => ReadAtUtc ??= nowUtc;
}

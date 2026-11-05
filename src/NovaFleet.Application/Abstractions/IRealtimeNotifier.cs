namespace NovaFleet.Application.Abstractions;

/// <summary>پیام زنده اعلان داخلی.</summary>
public sealed record RealtimeMessage(
    Guid NotificationId,
    string Title,
    string Message,
    string EventKey,
    string? ReferenceType,
    string? ReferenceId,
    DateTimeOffset CreatedAtUtc);

/// <summary>ارسال اعلان زنده به کاربران مجاز و انتشار رخدادهای بی‌اعتبارسازی داده.</summary>
public interface IRealtimeNotifier
{
    Task NotifyUsersAsync(IEnumerable<string> usernames, RealtimeMessage message, CancellationToken cancellationToken = default);

    /// <summary>انتشار رخداد به گروهی از کاربران؛ payload باید بدون داده حساس باشد.</summary>
    Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default);
}

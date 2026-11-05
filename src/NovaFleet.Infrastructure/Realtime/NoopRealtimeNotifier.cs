using NovaFleet.Application.Abstractions;

namespace NovaFleet.Infrastructure.Realtime;

/// <summary>اعلان زنده تا زمان اتصال SignalR؛ اعلان‌ها همچنان در پایگاه داده ذخیره می‌شوند و رابط کاربری با poll می‌خواند.</summary>
public sealed class NoopRealtimeNotifier : IRealtimeNotifier
{
    public Task NotifyUsersAsync(IEnumerable<string> usernames, RealtimeMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

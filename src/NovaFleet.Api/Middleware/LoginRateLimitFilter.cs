using System.Globalization;
using System.Net;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;

namespace NovaFleet.Api.Middleware;

internal sealed class LoginRateLimitFilter(LoginFailureRateLimiter limiter) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.GetArgument<LoginRequest>(0);
        var clientIp = context.HttpContext.Connection.RemoteIpAddress?.ToString();
        var retryAfter = limiter.GetRetryAfter(request.Username, clientIp);
        if (retryAfter is { } delay)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return Results.Json(
                new { message = "تلاش‌های ناموفق بیش از حد مجاز است؛ کمی بعد دوباره تلاش کنید." },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var result = await next(context);
            if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden })
            {
                limiter.RecordFailure(request.Username, clientIp);
            }

            return result;
        }
        catch (UnauthorizedAppException)
        {
            limiter.RecordFailure(request.Username, clientIp);
            throw;
        }
        catch (ForbiddenAppException)
        {
            limiter.RecordFailure(request.Username, clientIp);
            throw;
        }
    }
}

// shortcut: counters are process-local, use a shared limiter before scaling to multiple instances.
internal sealed class LoginFailureRateLimiter(TimeProvider timeProvider)
{
    private const int AccountFailureLimit = 5;
    private const int IpFailureLimit = 20;
    private static readonly TimeSpan AccountWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IpWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly Dictionary<string, FailureWindow> _accountWindows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FailureWindow> _ipWindows = new(StringComparer.Ordinal);
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    public TimeSpan? GetRetryAfter(string? username, string? clientIp)
    {
        var now = timeProvider.GetUtcNow();
        var accountKey = $"{NormalizeUsername(username)}\u001f{NormalizeIp(clientIp)}";
        var ipKey = NormalizeIp(clientIp);
        lock (_gate)
        {
            Cleanup(now);
            var account = GetWindow(_accountWindows, accountKey, now, AccountWindow);
            if (account.Count >= AccountFailureLimit)
            {
                return account.ResetAt - now;
            }

            var ip = GetWindow(_ipWindows, ipKey, now, IpWindow);
            return ip.Count >= IpFailureLimit ? ip.ResetAt - now : null;
        }
    }

    public void RecordFailure(string? username, string? clientIp)
    {
        var now = timeProvider.GetUtcNow();
        var accountKey = $"{NormalizeUsername(username)}\u001f{NormalizeIp(clientIp)}";
        var ipKey = NormalizeIp(clientIp);
        lock (_gate)
        {
            Cleanup(now);
            GetWindow(_accountWindows, accountKey, now, AccountWindow).Count++;
            GetWindow(_ipWindows, ipKey, now, IpWindow).Count++;
        }
    }

    private void Cleanup(DateTimeOffset now)
    {
        if (now < _nextCleanup)
        {
            return;
        }

        RemoveExpired(_accountWindows, now);
        RemoveExpired(_ipWindows, now);
        _nextCleanup = now.Add(CleanupInterval);
    }

    private static FailureWindow GetWindow(
        Dictionary<string, FailureWindow> windows,
        string key,
        DateTimeOffset now,
        TimeSpan duration)
    {
        if (!windows.TryGetValue(key, out var window) || now >= window.ResetAt)
        {
            window = new FailureWindow { ResetAt = now.Add(duration) };
            windows[key] = window;
        }

        return window;
    }

    private static void RemoveExpired(Dictionary<string, FailureWindow> windows, DateTimeOffset now)
    {
        var expired = windows.Where(item => item.Value.ResetAt <= now).Select(item => item.Key).ToList();
        foreach (var key in expired)
        {
            windows.Remove(key);
        }
    }

    private static string NormalizeUsername(string? username) =>
        string.IsNullOrWhiteSpace(username) ? "<empty>" : username.Trim().ToUpperInvariant();

    private static string NormalizeIp(string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return "<unknown>";
        }

        if (!IPAddress.TryParse(clientIp.Trim(), out var address))
        {
            return clientIp.Trim();
        }

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }

    private sealed class FailureWindow
    {
        public int Count { get; set; }
        public DateTimeOffset ResetAt { get; set; }
    }
}

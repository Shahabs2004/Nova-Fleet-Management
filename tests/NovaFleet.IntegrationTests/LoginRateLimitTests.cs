using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NovaFleet.Api.Middleware;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;

namespace NovaFleet.IntegrationTests;

public sealed class LoginRateLimitTests
{
    [Fact]
    public async Task FiveFailures_BlockSixthAttemptForSameUsernameAndIp()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-23T10:00:00Z"));
        var filter = new LoginRateLimitFilter(new LoginFailureRateLimiter(time));
        var calls = 0;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await AssertFailure(filter, " user ", "192.0.2.10", () => calls++);
        }

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.10");
        var result = await filter.InvokeAsync(
            new TestFilterContext(context, new LoginRequest("USER", "wrong")),
            _ => throw new InvalidOperationException("Blocked request reached login."));

        Assert.NotNull(result);
        await ExecuteAsync(context, result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("60", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task AccountLimit_UsesNormalizedUsernameAndClientIpPartitions()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-23T10:00:00Z"));
        var filter = new LoginRateLimitFilter(new LoginFailureRateLimiter(time));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await AssertFailure(filter, "user", "192.0.2.10");
        }

        await AssertFailure(filter, "user", "192.0.2.11");
        await AssertFailure(filter, "other", "192.0.2.10");

        var mapped = Context("user", "::ffff:192.0.2.10");
        var blocked = await filter.InvokeAsync(mapped, _ => throw new InvalidOperationException("Blocked request reached login."));
        Assert.NotNull(blocked);
        await ExecuteAsync(mapped.HttpContext, blocked);
        Assert.Equal(StatusCodes.Status429TooManyRequests, mapped.HttpContext.Response.StatusCode);
        await AssertFailure(filter, "user", "2001:db8::1");
    }

    [Fact]
    public async Task TwentyFailures_BlockTwentyFirstAttemptForSharedIp()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-23T10:00:00Z"));
        var filter = new LoginRateLimitFilter(new LoginFailureRateLimiter(time));

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            await AssertFailure(filter, $"user{attempt}", "192.0.2.10");
        }

        var context = Context("user21", "192.0.2.10");
        var result = await filter.InvokeAsync(context, _ => throw new InvalidOperationException("Blocked request reached login."));

        Assert.NotNull(result);
        await ExecuteAsync(context.HttpContext, result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.HttpContext.Response.StatusCode);
        Assert.Equal("3600", context.HttpContext.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task SuccessfulLogins_DoNotConsumeFailurePermits()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-23T10:00:00Z"));
        var filter = new LoginRateLimitFilter(new LoginFailureRateLimiter(time));

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var result = await filter.InvokeAsync(Context("user", "192.0.2.10"), _ => ValueTask.FromResult<object?>(Results.Ok()));
            Assert.NotNull(result);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await AssertFailure(filter, "user", "192.0.2.10");
        }
    }

    [Fact]
    public async Task FixedWindows_ResetAfterOneMinuteAndOneHour()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-23T10:00:00Z"));
        var filter = new LoginRateLimitFilter(new LoginFailureRateLimiter(time));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await AssertFailure(filter, "user", "192.0.2.10");
        }
        time.Now = time.Now.AddMinutes(1);
        await AssertFailure(filter, "user", "192.0.2.10");

        for (var attempt = 1; attempt <= 14; attempt++)
        {
            await AssertFailure(filter, $"other{attempt}", "192.0.2.10");
        }
        var blockedContext = Context("blocked", "192.0.2.10");
        var blocked = await filter.InvokeAsync(blockedContext, _ => ValueTask.FromResult<object?>(Results.Ok()));
        Assert.NotNull(blocked);
        await ExecuteAsync(blockedContext.HttpContext, blocked);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blockedContext.HttpContext.Response.StatusCode);

        time.Now = time.Now.AddHours(1);
        await AssertFailure(filter, "new-user", "192.0.2.10");
    }

    private static async Task ExecuteAsync(HttpContext context, object? result)
    {
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        await Assert.IsAssignableFrom<IResult>(result).ExecuteAsync(context);
    }

    private static async Task AssertFailure(
        LoginRateLimitFilter filter,
        string username,
        string ip,
        Action? onCall = null)
    {
        await Assert.ThrowsAsync<UnauthorizedAppException>(async () =>
        {
            onCall?.Invoke();
            await filter.InvokeAsync(Context(username, ip), _ => throw new UnauthorizedAppException("invalid"));
        });
    }

    private static TestFilterContext Context(string username, string ip)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        return new TestFilterContext(http, new LoginRequest(username, "wrong"));
    }

    private sealed class TestFilterContext(DefaultHttpContext http, LoginRequest request) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext => http;
        public override IList<object?> Arguments => [request];

        public override T GetArgument<T>(int index) => index == 0 && request is T value ? value : default!;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

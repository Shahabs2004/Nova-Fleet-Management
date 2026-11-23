using System.Net;
using Microsoft.AspNetCore.Http;
using NovaFleet.Api.Middleware;

namespace NovaFleet.IntegrationTests;

public sealed class CsrfProtectionTests
{
    [Theory]
    [InlineData("/api/auth/login-local")]
    [InlineData("/api/auth/login-ldap")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/setup/status")]
    public async Task AnonymousOrSafeRequests_DoNotRequireCsrfHeader(string path)
    {
        var method = path.EndsWith("/me", StringComparison.Ordinal) || path.EndsWith("/status", StringComparison.Ordinal)
            ? "GET"
            : "POST";

        var (status, called) = await InvokeAsync(path, method, "192.0.2.10");

        Assert.Equal(StatusCodes.Status204NoContent, status);
        Assert.True(called);
    }

    [Theory]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/auth/change-password")]
    [InlineData("/api/auth/unknown-mutation")]
    public async Task AuthenticatedAuthMutations_RejectMissingCsrfHeader(string path)
    {
        var (status, called) = await InvokeAsync(path, "POST", "192.0.2.10");

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.False(called);
    }

    [Theory]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/auth/change-password")]
    public async Task AuthenticatedAuthMutations_AllowExplicitCsrfHeader(string path)
    {
        var (status, called) = await InvokeAsync(path, "POST", "192.0.2.10", "XMLHttpRequest");

        Assert.Equal(StatusCodes.Status204NoContent, status);
        Assert.True(called);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task SetupMutation_OnLoopbackRemainsExempt(string remoteAddress)
    {
        var (status, called) = await InvokeAsync("/api/setup/initialize", "POST", remoteAddress);

        Assert.Equal(StatusCodes.Status204NoContent, status);
        Assert.True(called);
    }

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData(null)]
    public async Task SetupMutation_FromRemoteRequiresCsrfHeader(string? remoteAddress)
    {
        var (status, called) = await InvokeAsync("/api/setup/initialize", "POST", remoteAddress);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.False(called);
    }

    [Fact]
    public async Task SetupMutation_FromRemoteWithHeaderPassesCsrfMiddleware()
    {
        var (status, called) = await InvokeAsync("/api/setup/initialize", "POST", "192.0.2.10", "XMLHttpRequest");

        Assert.Equal(StatusCodes.Status204NoContent, status);
        Assert.True(called);
    }

    private static async Task<(int Status, bool Called)> InvokeAsync(
        string path,
        string method,
        string? remoteAddress,
        string? csrfHeader = null)
    {
        var called = false;
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);
        if (csrfHeader is not null)
        {
            context.Request.Headers["X-Requested-With"] = csrfHeader;
        }

        var middleware = new CsrfHeaderMiddleware(httpContext =>
        {
            called = true;
            httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, called);
    }
}

using Microsoft.AspNetCore.Http;
using NovaFleet.Api.Middleware;

namespace NovaFleet.IntegrationTests;

public sealed class InstallerVerificationMiddlewareTests
{
    [Theory]
    [InlineData("GET", "/health", true)]
    [InlineData("POST", "/health", false)]
    [InlineData("GET", "/", false)]
    [InlineData("POST", "/api/missions", false)]
    [InlineData("POST", "/api/setup", false)]
    [InlineData("GET", "/api/dashboard", false)]
    public async Task OnlyReadOnlyHealthCanReachTheApplication(string method, string path, bool allowed)
    {
        var reachedApplication = false;
        var middleware = new InstallerVerificationMiddleware(_ =>
        {
            reachedApplication = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(allowed, reachedApplication);
        Assert.Equal(allowed ? 200 : 503, context.Response.StatusCode);
        if (!allowed) Assert.Equal("120", context.Response.Headers.RetryAfter.ToString());
    }
}

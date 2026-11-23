using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Api.Endpoints;
using NovaFleet.Api.Identity;
using NovaFleet.Api.Middleware;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Auditing;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Identity;
using NovaFleet.Application.Sessions;
using NovaFleet.Domain.Identity;

namespace NovaFleet.IntegrationTests;

public sealed class DirectoryGroupAuthorizationTests
{
    [Theory]
    [InlineData(null, StatusCodes.Status401Unauthorized, 0)]
    [InlineData("security.sessions.view", StatusCodes.Status403Forbidden, 0)]
    [InlineData(PermissionCatalog.Administration.AuthorizationManage, StatusCodes.Status200OK, 1)]
    public async Task DirectoryGroupEnumeration_RequiresAuthorizationPermission(
        string? permission,
        int expectedStatus,
        int expectedDirectoryCalls)
    {
        var directory = new RecordingDirectoryAuthenticator();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddScoped<CurrentUserHolder>();
        builder.Services.AddSingleton<IDirectoryAuthenticator>(directory);
        builder.Services.AddSingleton<IUserService>(_ => throw new InvalidOperationException());
        builder.Services.AddSingleton<ISessionService>(_ => throw new InvalidOperationException());
        builder.Services.AddSingleton<IAuditQueryService>(_ => throw new InvalidOperationException());
        builder.Services.AddSingleton<IRoleService>(_ => throw new InvalidOperationException());
        await using var app = builder.Build();
        app.MapGroup("/api").MapSecurityEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single(item => item is RouteEndpoint route
                && route.RoutePattern.RawText == "/api/roles/directory-groups");
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response = { Body = new MemoryStream() }
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/roles/directory-groups";
        if (permission is not null)
        {
            context.RequestServices.GetRequiredService<CurrentUserHolder>().User = new TestCurrentUser(permission);
        }

        var middleware = new ApiExceptionHandlerMiddleware(
            httpContext => endpoint.RequestDelegate!(httpContext),
            NullLogger<ApiExceptionHandlerMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedDirectoryCalls, directory.CallCount);
    }

    private sealed class TestCurrentUser(string permission) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string Username => "test.user";
        public string DisplayName => "Test User";
        public bool IsLocalAdmin => false;
        public EffectivePermissionSet Permissions => EffectivePermissionSet.Build([(permission, PermissionEffect.Grant)]);
    }

    private sealed class RecordingDirectoryAuthenticator : IDirectoryAuthenticator
    {
        public int CallCount { get; private set; }

        public Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DirectoryProbeResult> ProbeAsync(string? sampleUsername, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DirectoryGroupsResult> ListGroupsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(DirectoryGroupsResult.Loaded(
                [new DirectoryGroupInfo("NovaFleet-Test", "CN=NovaFleet-Test,DC=example,DC=test")], false));
        }
    }
}

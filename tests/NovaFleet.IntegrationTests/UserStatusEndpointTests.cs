using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NovaFleet.Api.Endpoints;
using NovaFleet.Api.Identity;
using NovaFleet.Api.Middleware;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Auditing;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Application.Sessions;

namespace NovaFleet.IntegrationTests;

public sealed class UserStatusEndpointTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UserStatusMutation_MapsActiveFlagToDisabledArgument(bool active, bool expectedDisabled)
    {
        var userId = Guid.NewGuid();
        var service = new RecordingUserService();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<CurrentUserHolder>();
        builder.Services.AddSingleton<IUserService>(service);
        builder.Services.AddSingleton<ISessionService, ThrowingSessionService>();
        builder.Services.AddSingleton<IAuditQueryService, ThrowingAuditQueryService>();
        builder.Services.AddSingleton<IRoleService, ThrowingRoleService>();
        builder.Services.AddSingleton<IDirectoryAuthenticator, ThrowingDirectoryAuthenticator>();
        await using var app = builder.Build();
        app.MapGroup("/api").MapSecurityEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single(item => item is RouteEndpoint route
                && route.RoutePattern.RawText == "/api/users/{id:guid}/disabled");
        var payload = Encoding.UTF8.GetBytes($"{{\"active\":{(active ? "true" : "false")}}}");
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response = { Body = new MemoryStream() }
        };
        context.Request.Method = "POST";
        context.Request.Path = $"/api/users/{userId}/disabled";
        context.Request.RouteValues["id"] = userId.ToString();
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = payload.Length;
        context.Request.Body = new MemoryStream(payload);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature { CanHaveBody = true });

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(expectedDisabled, service.Disabled);
    }

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody { get; set; }
    }

    private sealed class RecordingUserService : IUserService
    {
        public Guid? UserId { get; private set; }
        public bool? Disabled { get; private set; }

        public Task<UserDetailsDto> SetDisabledAsync(Guid id, bool disabled, CancellationToken cancellationToken = default)
        {
            UserId = id;
            Disabled = disabled;
            return Task.FromResult(new UserDetailsDto(
                id, "test.user", "Test", "User", null, null, null, disabled, false, false,
                DateTimeOffset.UtcNow, null, [], [], []));
        }

        public Task<ProfileDto> GetProfileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProfileDto> UpdateProfilePhotoAsync(UpdateProfilePhotoRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserListItemDto>> ListAsync(string? search, bool includeDisabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserDetailsDto> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserDetailsDto> CreateLocalAsync(CreateLocalUserRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserDetailsDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPermissionsAsync(Guid id, SetPermissionsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingSessionService : ISessionService
    {
        public Task<PagedResult<UserSessionDto>> ListAsync(SessionQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ValidateAsync(string sessionKey, int absoluteMinutes, int idleMinutes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ForceEndAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ActiveCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingAuditQueryService : IAuditQueryService
    {
        public Task<PagedResult<AuditEntryDto>> ListAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingRoleService : IRoleService
    {
        public Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RoleDto> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RoleDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPermissionsAsync(Guid id, SetPermissionsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DirectoryMappingDto>> ListDirectoryMappingsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DirectoryMappingDto> SaveDirectoryMappingAsync(SaveDirectoryMappingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveDirectoryMappingAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DefaultRoleDto> GetDefaultRoleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DefaultRoleDto> SetDefaultRoleAsync(SetDefaultRoleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingDirectoryAuthenticator : IDirectoryAuthenticator
    {
        public Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DirectoryProbeResult> ProbeAsync(string? sampleUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DirectoryGroupsResult> ListGroupsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

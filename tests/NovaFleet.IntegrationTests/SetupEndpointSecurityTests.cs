using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Api.Endpoints;
using NovaFleet.Api.Middleware;
using NovaFleet.Application.Common;
using NovaFleet.Application.Setup;
using NovaFleet.Infrastructure.Setup;

namespace NovaFleet.IntegrationTests;

public sealed class SetupEndpointSecurityTests
{
    private const string DatabaseBody = """
        {"serverName":"localhost","databaseName":"NovaFleet_Endpoint_Test","useWindowsAuthentication":true,"userName":null,"password":null,"trustServerCertificate":true}
        """;

    private const string InitializeBody = """
        {"organizationName":"سازمان آزمون","adminUsername":"endpoint.admin","adminPassword":"Setup#2026-Pass","adminFirstName":"مدیر","adminLastName":"آزمون","adminMobile":"09121112233"}
        """;

    [Theory]
    [InlineData("/api/setup/database", DatabaseBody, "192.0.2.10", false)]
    [InlineData("/api/setup/initialize", InitializeBody, "192.0.2.10", false)]
    [InlineData("/api/setup/database", DatabaseBody, "192.0.2.10", true)]
    [InlineData("/api/setup/initialize", InitializeBody, "192.0.2.10", true)]
    [InlineData("/api/setup/database", DatabaseBody, null, false)]
    [InlineData("/api/setup/initialize", InitializeBody, null, false)]
    public async Task SetupMutation_RejectsRemoteOrMissingConnectionAddressBeforeServiceCall(
        string path,
        string body,
        string? remoteAddress,
        bool claimsLoopback)
    {
        var (status, response, service) = await InvokeAsync(path, body, remoteAddress, claimsLoopback);

        Assert.True(status == StatusCodes.Status403Forbidden, $"Expected 403, got {status}: {response}");
        Assert.Equal(0, service.MutationCount);
    }

    [Theory]
    [InlineData("/api/setup/database", DatabaseBody, "127.0.0.1")]
    [InlineData("/api/setup/initialize", InitializeBody, "127.0.0.1")]
    [InlineData("/api/setup/database", DatabaseBody, "::1")]
    [InlineData("/api/setup/initialize", InitializeBody, "::1")]
    [InlineData("/api/setup/database", DatabaseBody, "::ffff:127.0.0.1")]
    [InlineData("/api/setup/initialize", InitializeBody, "::ffff:127.0.0.1")]
    public async Task SetupMutation_AllowsLoopbackConnection(string path, string body, string remoteAddress)
    {
        var (status, response, service) = await InvokeAsync(path, body, remoteAddress);

        Assert.True(status == StatusCodes.Status200OK, $"Expected 200, got {status}: {response}");
        Assert.Equal(1, service.MutationCount);
    }

    [Theory]
    [InlineData("/api/setup/database", DatabaseBody)]
    [InlineData("/api/setup/initialize", InitializeBody)]
    public async Task SetupMutation_OnInstalledDatabaseStillReturnsConflict(string path, string body)
    {
        var (status, response, _) = await InvokeAsync(path, body, "127.0.0.1", installed: true);

        Assert.True(status == StatusCodes.Status409Conflict, $"Expected 409, got {status}: {response}");
    }

    [Theory]
    [InlineData("/api/setup/status")]
    [InlineData("/api/setup/database")]
    public async Task SetupStatus_RemainsReadableFromRemoteWithoutMutation(string path)
    {
        var (status, response, service) = await InvokeAsync(path, "{}", "192.0.2.10", method: "GET");

        Assert.True(status == StatusCodes.Status200OK, $"Expected 200, got {status}: {response}");
        Assert.Equal(0, service.MutationCount);
    }

    private static async Task<(int Status, string Body, RecordingSetupService Service)> InvokeAsync(
        string path,
        string body,
        string? remoteAddress,
        bool claimsLoopback = false,
        bool installed = false,
        string method = "POST")
    {
        var service = new RecordingSetupService(installed);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ISetupService>(service);
        await using var app = builder.Build();
        app.MapGroup("/api").MapSetupEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single(item => item is RouteEndpoint route
                && route.RoutePattern.RawText == path
                && route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true);
        var payload = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = payload.Length;
        context.Request.Body = new MemoryStream(payload);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature { CanHaveBody = true });
        context.Connection.RemoteIpAddress = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);
        if (claimsLoopback)
        {
            context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        }

        var middleware = new ApiExceptionHandlerMiddleware(
            httpContext => endpoint.RequestDelegate!(httpContext),
            NullLogger<ApiExceptionHandlerMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var response = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, response, service);
    }

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody { get; set; }
    }

    private sealed class RecordingSetupService(bool installed) : ISetupService
    {
        public int MutationCount { get; private set; }

        public Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SetupStatusDto(installed, installed, installed ? 1 : 0, null));

        public Task<SetupDatabaseStateDto> GetDatabaseStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SetupDatabaseStateDto(false, nameof(ConnectionSource.None), null, null, false, null, false));

        public Task<SetupDatabaseResultDto> ConfigureDatabaseAsync(
            SetupDatabaseRequest request,
            CancellationToken cancellationToken = default)
        {
            MutationCount++;
            if (installed)
            {
                throw new ConflictAppException("راه‌اندازی اولیه قبلاً انجام شده است.");
            }

            return Task.FromResult(new SetupDatabaseResultDto(false, false, true, "آماده است."));
        }

        public Task<SetupStatusDto> InitializeAsync(SetupRequest request, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            if (installed)
            {
                throw new ConflictAppException("راه‌اندازی اولیه قبلاً انجام شده است.");
            }

            return Task.FromResult(new SetupStatusDto(true, true, 1, request.OrganizationName));
        }
    }
}

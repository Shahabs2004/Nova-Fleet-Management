using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovaFleet.Api.Endpoints;
using NovaFleet.Application.Settings;
using NovaFleet.Infrastructure.Settings;

namespace NovaFleet.IntegrationTests;

public sealed class SettingsRouteCleanupTests
{
    [Fact]
    public async Task ConnectionSettings_ExposeOnlyFunctionalCards()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IConnectionsSettingsService, ConnectionsSettingsService>();
        builder.Services.AddSingleton<IConnectionTestsService, ConnectionTestsService>();
        await using var app = builder.Build();
        app.MapGroup("/api").MapSettingsEndpoints();

        var paths = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(path => path?.StartsWith("/api/settings/connections/", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Contains("/api/settings/connections/database", paths);
        Assert.Contains("/api/settings/connections/directory", paths);
        Assert.Contains("/api/settings/connections/sms", paths);
        Assert.DoesNotContain(paths, path => path?.Contains("/operations", StringComparison.Ordinal) == true);
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Api.Endpoints;
using NovaFleet.Api.Identity;
using NovaFleet.Api.Middleware;
using NovaFleet.Api.Workers;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Auditing;
using NovaFleet.Application.Dashboard;
using NovaFleet.Application.Drivers;
using NovaFleet.Application.Identity;
using NovaFleet.Application.Locations;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Organization;
using NovaFleet.Application.Reports;
using NovaFleet.Application.Sessions;
using NovaFleet.Application.Settings;
using NovaFleet.Application.Setup;
using NovaFleet.Application.Timeline;
using NovaFleet.Application.Vehicles;
using Scalar.AspNetCore;
using NovaFleet.Infrastructure.Auditing;
using NovaFleet.Infrastructure.Dashboard;
using NovaFleet.Infrastructure.Directory;
using NovaFleet.Infrastructure.Drivers;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Locations;
using NovaFleet.Infrastructure.Missions;
using NovaFleet.Infrastructure.Notifications;
using NovaFleet.Infrastructure.Organization;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Realtime;
using NovaFleet.Infrastructure.Reports;
using NovaFleet.Infrastructure.Security;
using NovaFleet.Infrastructure.Sessions;
using NovaFleet.Infrastructure.Settings;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.Infrastructure.Setup;
using NovaFleet.Infrastructure.Timeline;
using NovaFleet.Infrastructure.Vehicles;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;
RegisterPersianFont();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("NOVAFLEET_");

var connectionString = ResolveConnectionString(builder.Configuration);
builder.Services.AddDbContext<NovaFleetDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.Configure<DirectoryOptions>(builder.Configuration.GetSection(DirectoryOptions.SectionName));
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenApi();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "nf_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = false;
        options.EventsType = typeof(SessionCookieEvents);
    });
builder.Services.AddSingleton<SessionCookieEvents>();
builder.Services.AddAuthorization();

// Application abstractions
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IRealtimeNotifier, NoopRealtimeNotifier>();
builder.Services.AddScoped<IPermissionResolver, PermissionResolver>();
builder.Services.AddScoped<IDirectoryAuthenticator, LdapDirectoryAuthenticator>();
builder.Services.AddScoped<IAuditTrail, AuditTrail>();
builder.Services.AddScoped<IRequestContext, HttpContextRequestContext>();
builder.Services.AddScoped<CurrentUserHolder>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserHolder>().User);

// SMS gateway: شبیه‌سازی پیش‌فرض؛ ADP فقط با تنظیم کامل محیط فعال می‌شود.
builder.Services.AddHttpClient("adp-sms");
builder.Services.AddScoped<ISmsGateway>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmsOptions>>().Value;
    return options.IsSimulation
        ? ActivatorUtilities.CreateInstance<SimulatedSmsGateway>(sp)
        : ActivatorUtilities.CreateInstance<AdpSmsGateway>(sp);
});

// Module services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationPublisher, NotificationPublisher>();
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<ISmsDispatcher, SmsDispatcher>();
builder.Services.AddScoped<IDriverService, DriverService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IOrganizationUnitService, OrganizationUnitService>();
builder.Services.AddScoped<IMissionService, MissionService>();
builder.Services.AddScoped<ISetupService, SetupService>();
builder.Services.AddScoped<ITimelineService, TimelineService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services.AddHostedService<SmsQueueWorker>();
builder.Services.AddHostedService<MissionLifecycleWorker>();

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseMiddleware<ApiExceptionHandlerMiddleware>();
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseAuthentication();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapApiEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();

static void RegisterPersianFont()
{
    var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf");
    if (File.Exists(fontPath))
    {
        using var stream = File.OpenRead(fontPath);
        FontManager.RegisterFontFromStream(stream);
    }
}

static string ResolveConnectionString(IConfiguration configuration)
{
    var configured = configuration.GetConnectionString("Default");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    var host = Environment.GetEnvironmentVariable("SQL_HOST");
    var database = Environment.GetEnvironmentVariable("DB_NAME");
    var login = Environment.GetEnvironmentVariable("APP_LOGIN");
    var password = Environment.GetEnvironmentVariable("APP_PASSWORD");
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
        || string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "رشته اتصال تنظیم نشده است؛ ConnectionStrings:Default یا متغیرهای SQL_HOST/DB_NAME/APP_LOGIN/APP_PASSWORD لازم است.");
    }

    return $"Server={host};Database={database};User ID={login};Password={password};TrustServerCertificate=True;MultipleActiveResultSets=True";
}

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NovaFleetDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
}

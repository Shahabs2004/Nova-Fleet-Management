using System.Data.Common;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Auditing;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Reports;
using NovaFleet.Application.Settings;
using NovaFleet.Application.Timeline;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Missions;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Reports;
using NovaFleet.Infrastructure.Security;
using NovaFleet.Infrastructure.Timeline;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace NovaFleet.IntegrationTests;

public sealed class MissionVisibilityIntegrationTests
{
    static MissionVisibilityIntegrationTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        using var regular = File.OpenRead(Path.Combine(fonts, "tahoma.ttf"));
        using var bold = File.OpenRead(Path.Combine(fonts, "tahomabd.ttf"));
        FontManager.RegisterFontFromStream(regular);
        FontManager.RegisterFontFromStream(bold);
    }

    private static readonly MissionQuery SearchQuery = new(
        null, null, null, null, null, null, null, null, false, 1, 100);
    private static readonly ReportQuery ReportQuery = new(
        null, null, null, null, null, null, null);

    [Fact]
    public async Task Search_OwnOnlySeesOwnRowsAndViewAllSeesBothRequesters()
    {
        using var fixture = new Fixture();
        var ownService = fixture.MissionServiceFor(fixture.OwnUser);
        var allService = fixture.MissionServiceFor(fixture.AllUser);

        var own = await ownService.SearchAsync(SearchQuery);
        var all = await allService.SearchAsync(SearchQuery);
        var onlyMine = await allService.SearchAsync(SearchQuery with { OnlyMine = true });

        Assert.Equal(2, own.TotalCount);
        Assert.All(own.Items, item =>
        {
            Assert.True(item.IsOwn);
            Assert.Contains("OWN-ONLY", item.Title, StringComparison.Ordinal);
        });
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(0, onlyMine.TotalCount);
        Assert.Empty(onlyMine.Items);
    }

    [Fact]
    public async Task DetailAndOrderPdf_UseTheSameVisibilityPredicate()
    {
        using var fixture = new Fixture();
        var ownService = fixture.MissionServiceFor(fixture.OwnUser);
        var allService = fixture.MissionServiceFor(fixture.AllUser);

        Assert.Equal("OWN-ONLY approved", (await ownService.GetAsync(fixture.OwnApproved.Id)).Title);
        Assert.Equal("OTHER-ONLY approved", (await allService.GetAsync(fixture.OtherApproved.Id)).Title);
        await Assert.ThrowsAsync<NotFoundAppException>(() => ownService.GetAsync(fixture.OtherApproved.Id));
        await Assert.ThrowsAsync<NotFoundAppException>(() => ownService.ExportOrderPdfAsync(fixture.OtherApproved.Id));
        Assert.NotEmpty(await ownService.ExportOrderPdfAsync(fixture.OwnApproved.Id));
        Assert.NotEmpty(await allService.ExportOrderPdfAsync(fixture.OtherApproved.Id));
    }

    [Fact]
    public async Task Timeline_OwnOnlySeesOwnMissionsAndViewAllSeesBothRequesters()
    {
        using var fixture = new Fixture();
        var from = Fixture.Hour(0, 0);
        var to = Fixture.Hour(0, 23);

        var own = await new TimelineService(fixture.Db, fixture.OwnUser).GetAsync(from, to, null, null);
        var all = await new TimelineService(fixture.Db, fixture.AllUser).GetAsync(from, to, null, null);

        Assert.Equal(2, own.Count);
        Assert.All(own, item => Assert.Contains("OWN-ONLY", item.Title, StringComparison.Ordinal));
        Assert.Equal(4, all.Count);
        Assert.Contains(all, item => item.Title == "OTHER-ONLY approved");
    }

    [Theory]
    [InlineData("missions", 2)]
    [InlineData("missions-distance", 1)]
    [InlineData("driver-activity", 1)]
    [InlineData("vehicle-usage", 1)]
    [InlineData("org-unit", 1)]
    [InlineData("mission-status", 2)]
    public async Task MissionReports_OwnOnlyRowsAndTotalsExcludeOtherRequester(string key, int expectedRows)
    {
        using var fixture = new Fixture();
        var service = new ReportService(fixture.Db, fixture.OwnUser);

        var table = await service.RunAsync(key, ReportQuery);

        Assert.Equal(expectedRows, table.Rows.Count);
        AssertNoOtherRequesterData(table);
        if (key is "missions" or "org-unit" or "mission-status")
            Assert.Equal(2, table.Totals.Values.First());
    }

    [Theory]
    [InlineData("missions", 4)]
    [InlineData("missions-distance", 2)]
    [InlineData("driver-activity", 2)]
    [InlineData("vehicle-usage", 2)]
    [InlineData("org-unit", 2)]
    [InlineData("mission-status", 2)]
    public async Task MissionReports_ViewAllIncludesBothRequesters(string key, int expectedRows)
    {
        using var fixture = new Fixture();
        var service = new ReportService(fixture.Db, fixture.AllUser);

        var table = await service.RunAsync(key, ReportQuery);

        Assert.Equal(expectedRows, table.Rows.Count);
    }

    [Theory]
    [InlineData("missions")]
    [InlineData("missions-distance")]
    [InlineData("driver-activity")]
    [InlineData("vehicle-usage")]
    [InlineData("org-unit")]
    [InlineData("mission-status")]
    public async Task MissionReportExports_OwnOnlyNeverContainOtherRequesterData(string key)
    {
        using var fixture = new Fixture();
        var service = new ReportService(fixture.Db, fixture.OwnUser);

        var excel = await service.ExportExcelAsync(key, ReportQuery);
        var pdf = await service.ExportPdfAsync(key, ReportQuery);

        Assert.NotEmpty(excel);
        Assert.NotEmpty(pdf);
        using var stream = new MemoryStream(excel);
        using var workbook = new XLWorkbook(stream);
        var text = string.Join("\n", workbook.Worksheets.SelectMany(sheet =>
            sheet.RowsUsed().CellsUsed().Select(cell => cell.GetFormattedString())));
        Assert.DoesNotContain("OTHER-ONLY", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.OtherVehicle.PlateDisplay, text, StringComparison.Ordinal);
    }

    private static void AssertNoOtherRequesterData(ReportTableDto table)
    {
        var text = string.Join("\n", table.Rows.SelectMany(row => row).Select(value => value?.ToString()));
        Assert.DoesNotContain("OTHER-ONLY", text, StringComparison.Ordinal);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection;
        private readonly FakeSettingsService settings = new();
        private int missionSequence;

        public Fixture()
        {
            connection = new SqliteConnection("Filename=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<NovaFleetDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
                .Options;
            Db = new NovaFleetDbContext(options);
            Db.Database.EnsureCreated();

            OwnDriver = new Driver("OWN-ONLY", "driver", "09121112233");
            OtherDriver = new Driver("OTHER-ONLY", "driver", "09121112234");
            OwnVehicle = new Vehicle("OWN-ONLY", "model", Plate(10));
            OtherVehicle = new Vehicle("OTHER-ONLY", "model", Plate(11));
            Db.Drivers.AddRange(OwnDriver, OtherDriver);
            Db.Vehicles.AddRange(OwnVehicle, OtherVehicle);
            Db.SaveChanges();

            OwnPending = AddMission("own.requester", "OWN-ONLY pending", Hour(0, 10), Hour(0, 14));
            OtherPending = AddMission("other.requester", "OTHER-ONLY pending", Hour(0, 11), Hour(0, 15));
            OwnApproved = AddMission("own.requester", "OWN-ONLY approved", Hour(0, 16), Hour(0, 20));
            OtherApproved = AddMission("other.requester", "OTHER-ONLY approved", Hour(0, 17), Hour(0, 21));
            OwnApproved.Approve(OwnDriver.Id, OwnVehicle.Id, "approver", Hour(0, 9));
            OtherApproved.Approve(OtherDriver.Id, OtherVehicle.Id, "approver", Hour(0, 9));
            OwnApproved.SetOdometerStart(1_000);
            OwnApproved.SetOdometerEnd(1_025);
            OtherApproved.SetOdometerStart(2_000);
            OtherApproved.SetOdometerEnd(2_065);
            Db.SaveChangesAsync().GetAwaiter().GetResult();

            OwnUser = new TestCurrentUser("own.requester", PermissionCatalog.Missions.ViewOwn);
            AllUser = new TestCurrentUser("all.requester", PermissionCatalog.Missions.View);
        }

        public NovaFleetDbContext Db { get; }
        public Driver OwnDriver { get; }
        public Driver OtherDriver { get; }
        public Vehicle OwnVehicle { get; }
        public Vehicle OtherVehicle { get; }
        public Mission OwnPending { get; }
        public Mission OtherPending { get; }
        public Mission OwnApproved { get; }
        public Mission OtherApproved { get; }
        public TestCurrentUser OwnUser { get; }
        public TestCurrentUser AllUser { get; }

        public static DateTimeOffset Hour(int day, int hour) =>
            new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.FromMinutes(210)).AddDays(day).AddHours(hour);

        public MissionService MissionServiceFor(ICurrentUser user) =>
            new(Db, user, new FakeAuditTrail(), new FakeClock(), settings, new FakeNotifications(), new FakeSmsDispatcher());

        private Mission AddMission(string username, string title, DateTimeOffset start, DateTimeOffset end)
        {
            var mission = new Mission($"1405-{++missionSequence:D4}", title, username,
                username == "own.requester" ? "OWN-ONLY requester" : "OTHER-ONLY requester",
                "09123456789", username == "own.requester" ? "OWN-ONLY unit" : "OTHER-ONLY unit",
                MissionType.Freight, null, "شیراز", "تهران", null, start, end);
            Db.Missions.Add(mission);
            return mission;
        }

        private static IranianLicensePlate Plate(int firstTwo) =>
            IranianLicensePlate.Create($"{firstTwo:00}", "ب", "100", "40");

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }

    private sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        private static readonly ValueConverter DateTimeToUnixMilliseconds = new ValueConverter<DateTimeOffset, long>(
            value => value.ToUnixTimeMilliseconds(),
            value => DateTimeOffset.FromUnixTimeMilliseconds(value));

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property[RelationalAnnotationNames.ColumnType] is string columnType
                        && columnType.Contains("(max)", StringComparison.OrdinalIgnoreCase))
                    {
                        property[RelationalAnnotationNames.ColumnType] = null;
                    }

                    if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(DateTimeToUnixMilliseconds);
                    }

                    if (property.ClrType == typeof(byte[])
                        && property.ValueGenerated == ValueGenerated.OnAddOrUpdate
                        && property.IsConcurrencyToken)
                    {
                        property[RelationalAnnotationNames.DefaultValue] = new byte[1];
                        property.ValueGenerated = ValueGenerated.OnAdd;
                        property.IsConcurrencyToken = false;
                    }
                }
            }
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(string username, params string[] permissions)
        {
            Username = username;
            DisplayName = username;
            Permissions = EffectivePermissionSet.Build(
            [
                (PermissionCatalog.Reports.View, PermissionEffect.Grant),
                (PermissionCatalog.Reports.Export, PermissionEffect.Grant),
                (PermissionCatalog.Missions.Print, PermissionEffect.Grant),
                .. permissions.Select(permission => (permission, PermissionEffect.Grant))
            ]);
        }

        public bool IsAuthenticated => true;
        public string Username { get; }
        public string DisplayName { get; }
        public bool IsLocalAdmin => false;
        public EffectivePermissionSet Permissions { get; }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Fixture.Hour(0, 9);
    }

    private sealed class FakeAuditTrail : IAuditTrail
    {
        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeNotifications : INotificationPublisher
    {
        public Task PublishAsync(NotificationMessage message, IEnumerable<string> usernames, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishToPermissionAsync(NotificationMessage message, string permission, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishToRolesAsync(NotificationMessage message, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeSmsDispatcher : ISmsDispatcher
    {
        public Task QueueForMissionAsync(Guid missionId, SmsEventType eventType, string? reason, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RescheduleStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CancelStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> ProcessQueueAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> PollDeliveryAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<SettingsBundleDto> GetRuntimeBundleAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsBundleDto(
                new OrganizationProfileDto("ناوگان آزمون", null),
                new MissionRulesDto(15, 30, 24, 30, null),
                new SessionPolicyDto(480, 60),
                new MissionOrderTemplateDto(null, null, false)));

        public Task<SettingsBundleDto> GetBundleAsync(CancellationToken cancellationToken = default) => GetRuntimeBundleAsync(cancellationToken);
        public Task<OrganizationProfileDto> UpdateOrganizationAsync(OrganizationProfileDto profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<MissionRulesDto> UpdateMissionRulesAsync(MissionRulesDto rules, CancellationToken cancellationToken = default) => Task.FromResult(rules);
        public Task<SessionPolicyDto> UpdateSessionPolicyAsync(SessionPolicyDto policy, CancellationToken cancellationToken = default) => Task.FromResult(policy);
        public Task<MissionOrderTemplateDto> UpdateMissionOrderTemplateAsync(MissionOrderTemplateDto template, CancellationToken cancellationToken = default) => Task.FromResult(template);
        public Task<IReadOnlyList<SplitReasonDto>> ListSplitReasonsAsync(bool includeInactive, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SplitReasonDto>>([]);
        public Task<SplitReasonDto> CreateSplitReasonAsync(string title, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SplitReasonDto> UpdateSplitReasonAsync(Guid id, UpdateSplitReasonRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Reports;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Sessions;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Reports;

namespace NovaFleet.IntegrationTests;

public sealed class ReportSafetyTests
{
    [Fact]
    public async Task ExcelExport_KeepsFormulaLikeAndControlCharacterTextAsText()
    {
        using var fixture = new ReportFixture();
        var mission = fixture.AddMission("=1+1", fixture.Hour(0, 10), fixture.Hour(0, 11));
        mission.SetOdometerStart(100);
        mission.SetOdometerEnd(140);
        string[] payloads = ["+SUM(A1)", "-2", "@cmd", "tab\tvalue", "line\rbreak"];
        foreach (var payload in payloads)
        {
            var session = new UserSession("synthetic", UserSessionLoginMethod.Local, "127.0.0.1");
            session.End(fixture.Hour(0, 12), payload);
            fixture.Db.UserSessions.Add(session);
        }
        await fixture.Db.SaveChangesAsync();

        var missionExcel = await fixture.Service.ExportExcelAsync("missions", EmptyQuery);
        var sessionExcel = await fixture.Service.ExportExcelAsync("sessions", EmptyQuery);

        using var workbook = new XLWorkbook(new MemoryStream(missionExcel));
        var sheet = workbook.Worksheets.Single();
        Assert.Equal(XLDataType.Text, sheet.Cell(1, 1).DataType);
        Assert.Equal(XLDataType.Text, Assert.Single(sheet.RowsUsed().CellsUsed(), cell => cell.GetString() == "شماره").DataType);
        var formulaTitle = Assert.Single(sheet.RowsUsed().CellsUsed(), cell => cell.GetString() == "=1+1");
        Assert.Equal(XLDataType.Text, formulaTitle.DataType);
        var totals = Assert.Single(sheet.RowsUsed().CellsUsed(), cell => cell.GetString().StartsWith("تعداد:", StringComparison.Ordinal));
        Assert.Equal(XLDataType.Text, totals.DataType);
        Assert.Equal(XLDataType.Number, sheet.Cell(5, 9).DataType);
        Assert.Equal(40, sheet.Cell(5, 9).GetDouble());

        using var sessionWorkbook = new XLWorkbook(new MemoryStream(sessionExcel));
        var sessionSheet = sessionWorkbook.Worksheets.Single();
        foreach (var payload in payloads)
        {
            var stored = payload.Replace("\r", "_x000D_", StringComparison.Ordinal);
            var cell = Assert.Single(sessionSheet.RowsUsed().CellsUsed(), item => item.GetString() == stored);
            Assert.Equal(XLDataType.Text, cell.DataType);
            Assert.Equal(payload, cell.GetString().Replace("_x000D_", "\r", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task SmsReport_TotalsCoverAllRowsButTransferIsCapped()
    {
        using var fixture = new ReportFixture();
        fixture.AddSms(1005);

        var table = await fixture.Service.RunAsync("sms-log", EmptyQuery);

        Assert.Equal(1005, table.TotalCount);
        Assert.Equal(1000, table.Rows.Count);
        Assert.True(table.RowsTruncated);
        Assert.Equal(1000, table.MaxRows);
        Assert.Equal(1005, table.Totals["تعداد"]);
        Assert.Equal(1005, table.Totals["تحویل‌شده"]);
    }

    [Fact]
    public async Task SessionsReport_TotalsCoverAllRowsButTransferIsCapped()
    {
        using var fixture = new ReportFixture();
        for (var index = 0; index < 1005; index++)
            fixture.Db.UserSessions.Add(new UserSession("synthetic", UserSessionLoginMethod.Local, "127.0.0.1"));
        await fixture.Db.SaveChangesAsync();

        var table = await fixture.Service.RunAsync("sessions", EmptyQuery);

        Assert.Equal(1005, table.TotalCount);
        Assert.Equal(1000, table.Rows.Count);
        Assert.True(table.RowsTruncated);
        Assert.Equal(1000, table.MaxRows);
        Assert.Equal(1005, table.Totals["تعداد"]);
        Assert.Equal(1005, table.Totals["موفق"]);
    }

    [Fact]
    public async Task MissionReport_TransferCappedButTotalsCoverFullSet()
    {
        using var fixture = new ReportFixture();
        fixture.AddMissions(1001);

        var table = await fixture.Service.RunAsync("missions", EmptyQuery);

        Assert.Equal(1001, table.TotalCount);
        Assert.Equal(1000, table.Rows.Count);
        Assert.True(table.RowsTruncated);
        Assert.Equal(1000, table.MaxRows);
        Assert.Equal(1001, table.Totals["تعداد"]);
    }

    [Fact]
    public async Task SmsReport_FewerThanCapIsNotTruncated()
    {
        using var fixture = new ReportFixture();
        fixture.AddSms(3);

        var table = await fixture.Service.RunAsync("sms-log", EmptyQuery);

        Assert.Equal(3, table.TotalCount);
        Assert.Equal(3, table.Rows.Count);
        Assert.False(table.RowsTruncated);
        Assert.Equal(1000, table.MaxRows);
        Assert.Equal(3, table.Totals["تعداد"]);
    }

    [Fact]
    public async Task SmsReport_IncludesEndDay2300AndExcludesNextDay0000()
    {
        using var fixture = new ReportFixture();
        var boundary = fixture.Hour(0, 0).AddDays(1);
        fixture.AddSms(1, fixture.Hour(0, 23).AddMinutes(30), "=end-day");
        fixture.AddSms(1, boundary, "next-day");
        var query = EmptyQuery with { FromUtc = fixture.Hour(0, 0), ToUtc = boundary };

        var table = await fixture.Service.RunAsync("sms-log", query);

        Assert.Single(table.Rows);
        var row = Assert.Single(table.Rows, item => Equals(item[1], "=end-day") || Equals(item[1], "next-day"));
        Assert.Equal("=end-day", row[1]);
    }

    [Fact]
    public async Task Timeline_UsesHalfOpenOverlapAtBothRangeBoundaries()
    {
        using var fixture = new ReportFixture();
        fixture.AddMission("ends-at-start", fixture.Hour(0, 9), fixture.Hour(0, 10));
        fixture.AddMission("overlaps", fixture.Hour(0, 9), fixture.Hour(0, 11));
        fixture.AddMission("starts-at-end", fixture.Hour(0, 12), fixture.Hour(0, 13));

        var rows = await fixture.Timeline.GetAsync(fixture.Hour(0, 10), fixture.Hour(0, 12), null, null);

        Assert.Equal("overlaps", Assert.Single(rows).Title);
    }

    [Fact]
    public async Task Reports_RequireViewAndExportPermissionsIndependently()
    {
        using var fixture = new ReportFixture();
        var noPermissions = new ReportService(fixture.Db, new TestUser("denied"));
        var viewer = new ReportService(fixture.Db, new TestUser("viewer", PermissionCatalog.Reports.View));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => noPermissions.RunAsync("missions", EmptyQuery));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => viewer.ExportExcelAsync("missions", EmptyQuery));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => viewer.ExportPdfAsync("missions", EmptyQuery));
    }

    [Fact]
    public async Task MissionReport_StillRequiresMissionVisibilityPermission()
    {
        using var fixture = new ReportFixture();
        var reportOnly = new ReportService(fixture.Db, new TestUser("report-only",
            PermissionCatalog.Reports.View, PermissionCatalog.Reports.Export));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => reportOnly.RunAsync("missions", EmptyQuery));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => reportOnly.ExportExcelAsync("missions", EmptyQuery));
    }

    private static readonly ReportQuery EmptyQuery = new(null, null, null, null, null, null, null);

    private sealed class ReportFixture : IDisposable
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection connection;
        public ReportFixture()
        {
            connection = new("Filename=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<NovaFleetDbContext>()
                .UseSqlite(connection)
                .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer, SqliteCustomizer>()
                .Options;
            Db = new(options);
            Db.Database.EnsureCreated();
            User = new("reporter", PermissionCatalog.Reports.View, PermissionCatalog.Reports.Export,
                PermissionCatalog.Missions.View, PermissionCatalog.Notifications.SmsView,
                PermissionCatalog.Security.SessionsView);
            Service = new(Db, User);
            Timeline = new(Db, User);
        }

        public NovaFleetDbContext Db { get; }
        public TestUser User { get; }
        public ReportService Service { get; }
        public NovaFleet.Infrastructure.Timeline.TimelineService Timeline { get; }

        public DateTimeOffset Hour(int day, int hour) =>
            new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.FromMinutes(210)).AddDays(day).AddHours(hour);

        public void AddSms(int count, DateTimeOffset? createdAt = null, string recipientName = "synthetic")
        {
            for (var index = 0; index < count; index++)
            {
                var item = new SmsQueueItem(SmsEventType.MissionApproved, "09120000000", recipientName, "synthetic", nowUtc: createdAt ?? Hour(0, 0));
                typeof(NovaFleet.Domain.Common.Entity).GetProperty("CreatedAtUtc")!.SetValue(item, createdAt ?? Hour(0, 0));
                item.MarkSending(createdAt ?? Hour(0, 0));
                item.MarkAccepted("synthetic", createdAt ?? Hour(0, 0));
                item.MarkDelivered((createdAt ?? Hour(0, 0)).AddMinutes(1));
                Db.SmsQueueItems.Add(item);
            }
            Db.SaveChanges();
        }

        private int missionSequence;
        public void AddMissions(int count)
        {
            for (var index = 0; index < count; index++)
                Db.Missions.Add(new Mission($"T{++missionSequence:D5}", $"synthetic {index}", "reporter", "unit", "09120000000", "unit", MissionType.Freight, null, "a", "b", null, Hour(0, 10), Hour(0, 11)));
            Db.SaveChanges();
        }

        public Mission AddMission(string title, DateTimeOffset start, DateTimeOffset end)
        {
            var mission = new Mission($"T{++missionSequence:D5}", title, "reporter", "unit", "09120000000", "unit", MissionType.Freight, null, "a", "b", null, start, end);
            Db.Missions.Add(mission);
            Db.SaveChanges();
            return mission;
        }

        public void Dispose() { Db.Dispose(); connection.Dispose(); }
    }

    private sealed class TestUser : NovaFleet.Application.Abstractions.ICurrentUser
    {
        private readonly string username;

        public TestUser(string username, params string[] permissions)
        {
            this.username = username;
            Permissions = NovaFleet.Application.Authorization.EffectivePermissionSet.Build(
                permissions.Select(p => (p, NovaFleet.Domain.Identity.PermissionEffect.Grant)));
        }

        public string Username => username;
        public string DisplayName => username;
        public bool IsAuthenticated => true;
        public bool IsLocalAdmin => false;
        public NovaFleet.Application.Authorization.EffectivePermissionSet Permissions { get; }
    }

    private sealed class SqliteCustomizer(Microsoft.EntityFrameworkCore.Infrastructure.ModelCustomizerDependencies dependencies)
        : Microsoft.EntityFrameworkCore.Infrastructure.RelationalModelCustomizer(dependencies)
    {
        public override void Customize(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder, Microsoft.EntityFrameworkCore.DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                if (property[Microsoft.EntityFrameworkCore.Metadata.RelationalAnnotationNames.ColumnType] is string columnType
                    && columnType.Contains("(max)", StringComparison.OrdinalIgnoreCase))
                    property[Microsoft.EntityFrameworkCore.Metadata.RelationalAnnotationNames.ColumnType] = null;
                if ((System.Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset))
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v)));
                if (property.ClrType == typeof(byte[])
                    && property.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate
                    && property.IsConcurrencyToken)
                {
                    property[Microsoft.EntityFrameworkCore.Metadata.RelationalAnnotationNames.DefaultValue] = new byte[1];
                    property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd;
                    property.IsConcurrencyToken = false;
                }
            }
        }
    }
}

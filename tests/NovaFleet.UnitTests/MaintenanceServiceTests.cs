using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Maintenance;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Maintenance;
using NovaFleet.Infrastructure.Maintenance;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests;

public sealed class MaintenanceServiceTests : MissionTestBase
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private MaintenanceService Manager => new(Db, User(RequesterUsername, PermissionCatalog.Maintenance.All), Clock);

    private async Task<(Guid Vehicle, Guid Template)> SetupAsync()
    {
        var vehicle = NewVehicle();
        await Db.SaveChangesAsync();
        var template = await Manager.SaveTemplateAsync(null, new TemplateInput("روغن موتور", "روغن", "oil-can", 5000, 6, 1000, 30, true, 0), Ct);
        return (vehicle.Id, template.Id);
    }

    private static RecordInput Input(Guid template, long? km = 1000, Guid? schedule = null) => new(
        Guid.NewGuid(), Hour(-10, 8), km, "تعمیرگاه آزمون", null, 50,
        [new ItemInput(template, schedule, 100, [new PartInput("روغن", "چهار لیتر", 2, 300, null)])], 0, null);

    [Fact]
    public async Task RecordIsIdempotentAndUsesIntegerToman()
    {
        var (vehicle, template) = await SetupAsync();
        var input = Input(template);
        var first = await Manager.SaveRecordAsync(vehicle, null, input, Ct);
        var replay = await Manager.SaveRecordAsync(vehicle, null, input, Ct);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(750m, first.TotalCostToman);
        Assert.Single(await Db.MaintenanceRecords.ToListAsync());
        Assert.Single(await Db.VehicleOdometerReadings.ToListAsync());
        Assert.Equal(1000L, (await Manager.VehicleAsync(vehicle, Ct)).CurrentKm);
        await Assert.ThrowsAsync<ConflictAppException>(() => Manager.SaveRecordAsync(vehicle, null, input with { OtherCostToman = 51 }, Ct));
    }

    [Fact]
    public async Task ActualServiceResetsScheduleAndVoidRestoresBaseline()
    {
        var (vehicle, template) = await SetupAsync();
        var baseline = Hour(-60, 8);
        var schedule = await Manager.SaveScheduleAsync(vehicle, null, new ScheduleInput(template, "روغن", 5000, 6, 1000, 30, 100L, baseline, true, 0), Ct);
        var record = await Manager.SaveRecordAsync(vehicle, null, Input(template, 1000, schedule.Id), Ct);
        var after = (await Manager.VehicleAsync(vehicle, Ct)).Schedules.Single();
        Assert.Equal(6000L, after.DueKm);
        Assert.Equal(JalaliDate.AddMonths(record.PerformedAtUtc, 6), after.DueAtUtc);
        await Manager.VoidRecordAsync(vehicle, record.Id, new RevisionInput(record.Version, "ثبت اشتباه"), Ct);
        var restored = (await Manager.VehicleAsync(vehicle, Ct)).Schedules.Single();
        Assert.Equal(5100L, restored.DueKm);
        Assert.Equal(JalaliDate.AddMonths(baseline, 6), restored.DueAtUtc);
        Assert.Equal(0m, (await Manager.VehicleAsync(vehicle, Ct)).TotalCostToman);
        Assert.Single(await Manager.RevisionsAsync(vehicle, record.Id, Ct));
    }

    [Fact]
    public async Task CorrectionPreservesBeforeVersionAndRejectsStaleVersion()
    {
        var (vehicle, template) = await SetupAsync();
        var input = Input(template);
        var record = await Manager.SaveRecordAsync(vehicle, null, input, Ct);
        var corrected = await Manager.SaveRecordAsync(vehicle, record.Id, input with { Version = record.Version, OtherCostToman = 75, Reason = "اصلاح فاکتور" }, Ct);
        Assert.Equal(2, corrected.Version);
        Assert.Equal(775m, corrected.TotalCostToman);
        var revisions = await Manager.RevisionsAsync(vehicle, record.Id, Ct);
        Assert.Single(revisions);
        Assert.Contains("750", revisions[0].BeforeJson);
        await Assert.ThrowsAsync<ConflictAppException>(() => Manager.SaveRecordAsync(vehicle, record.Id, input with { Version = 1, Reason = "نسخه قدیمی" }, Ct));
    }

    [Fact]
    public async Task UnknownOdometerIsNotZeroAndDateOnlyScheduleHasNoInventedMileage()
    {
        var (vehicle, template) = await SetupAsync();
        var record = await Manager.SaveRecordAsync(vehicle, null, Input(template, null), Ct);
        Assert.Null(record.OdometerKm);
        Assert.Null((await Manager.OdometerAsync(vehicle, Ct)).CurrentKm);
        var schedule = await Manager.SaveScheduleAsync(vehicle, null, new ScheduleInput(template, "بازرسی", null, 6, 1000, 30, null, Hour(-1, 8), true, 0), Ct);
        Assert.Null(schedule.DueKm);
        Assert.Equal("healthy", schedule.State);
    }

    [Fact]
    public async Task BackwardsReadingRollsBack()
    {
        var (vehicle, template) = await SetupAsync();
        await Manager.SaveRecordAsync(vehicle, null, Input(template, 2000), Ct);
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-1, 8), 1500, 0, null), Ct));
        Db.ChangeTracker.Clear();
        Assert.Single(await Db.VehicleOdometerReadings.ToListAsync());
    }

    [Fact]
    public async Task ReportUsesMatchingRealReadingInterval()
    {
        var (vehicle, template) = await SetupAsync();
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-20, 8), 500, 0, null), Ct);
        await Manager.SaveRecordAsync(vehicle, null, Input(template, 1000), Ct);
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-1, 8), 2000, 0, null), Ct);
        var report = await Manager.ReportAsync(vehicle, Hour(-30, 8), Clock.UtcNow, Ct);
        Assert.Equal(1500L, report.DistanceKm);
        Assert.Equal(0.5m, report.CostPerKmToman);
        Assert.Equal(750m, report.Months.Sum(m => m.CostToman));
    }

    [Fact]
    public async Task TemplateUpdatesDoNotChangeVehicleIntervals()
    {
        var (vehicle, template) = await SetupAsync();
        await Manager.SaveScheduleAsync(vehicle, null, new ScheduleInput(template, "روغن", 5000, 6, 1000, 30, 0L, Hour(-1, 8), true, 0), Ct);
        await Manager.SaveTemplateAsync(template, new TemplateInput("روغن موتور", "روغن", "oil-can", 10000, 12, 500, 15, true, 1), Ct);
        var schedule = (await Manager.VehicleAsync(vehicle, Ct)).Schedules.Single();
        Assert.Equal(5000L, schedule.IntervalKm);
        Assert.Equal(6, schedule.IntervalMonths);
    }

    [Fact]
    public async Task OperatorCannotCorrectOrManageTemplates()
    {
        var (vehicle, template) = await SetupAsync();
        var op = new MaintenanceService(Db, User(OtherUsername, PermissionCatalog.Maintenance.View, PermissionCatalog.Maintenance.Create), Clock);
        var record = await op.SaveRecordAsync(vehicle, null, Input(template), Ct);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => op.VoidRecordAsync(vehicle, record.Id, new RevisionInput(1, "نامجاز"), Ct));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => op.SaveTemplateAsync(null, new TemplateInput("جدید", "دسته", "engine", 1000, null, 100, 10, true, 0), Ct));
    }

    [Fact]
    public async Task AttachmentChecksOwnershipAndFileSignature()
    {
        var (vehicle, template) = await SetupAsync();
        var record = await Manager.SaveRecordAsync(vehicle, null, Input(template), Ct);
        var pdf = "%PDF-1.4\nsynthetic fixture\n%%EOF"u8.ToArray();
        var file = await Manager.AddAttachmentAsync(vehicle, record.Id, "invoice.pdf", pdf, Ct);
        Assert.Equal(pdf, (await Manager.DownloadAsync(vehicle, record.Id, file.Id, Ct)).Content);
        await Assert.ThrowsAsync<NotFoundAppException>(() => Manager.DownloadAsync(Guid.NewGuid(), record.Id, file.Id, Ct));
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "invoice.png", pdf, Ct));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.5)]
    public void MoneyRejectsNegativeOrFractionalToman(decimal value) => Assert.Throws<ArgumentException>(() => MaintenanceRules.Money(value));

    [Theory]
    [InlineData(9000, "approaching")]
    [InlineData(10000, "due")]
    [InlineData(8000, "healthy")]
    public void OdometerWarningUsesInclusiveThreshold(long current, string expected) => Assert.Equal(expected,
        MaintenanceRules.DueState(10000, null, current, Hour(0, 8), 1000, 30, false));
}

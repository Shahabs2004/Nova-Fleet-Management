using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Maintenance;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Maintenance;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Security;
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
    public async Task DuplicateTemplateNameIsRejectedOnCreateAndRename()
    {
        await SetupAsync();
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.SaveTemplateAsync(null,
            new TemplateInput("روغن موتور", "روغن", "oil-can", 5000, 6, 1000, 30, true, 0), Ct));
        var other = await Manager.SaveTemplateAsync(null,
            new TemplateInput("فیلتر هوا", "فیلتر", "engine", 10000, 12, 500, 15, true, 0), Ct);
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.SaveTemplateAsync(other.Id,
            new TemplateInput("روغن موتور", "فیلتر", "engine", 10000, 12, 500, 15, true, other.Version), Ct));
        Assert.Equal(2, await Db.MaintenanceTemplates.CountAsync());
    }

    [Fact]
    public async Task FleetUnknownFilterPaginatesAcrossEvaluationBatches()
    {
        for (var i = 0; i < 51; i++) NewVehicle();
        await Db.SaveChangesAsync();
        var first = await Manager.FleetAsync(null, "unknown", 1, 12, Ct);
        Assert.Equal(51, first.TotalCount);
        Assert.Equal(12, first.Items.Count);
        var ids = new List<Guid>();
        for (var p = 1; p <= 5; p++)
            ids.AddRange((await Manager.FleetAsync(null, "unknown", p, 12, Ct)).Items.Select(v => v.VehicleId));
        Assert.Equal(51, ids.Count);
        Assert.Equal(51, ids.Distinct().Count());
        Assert.Equal(0, (await Manager.FleetAsync(null, "due", 1, 12, Ct)).TotalCount);
        Assert.Equal(0, (await Manager.FleetAsync(null, "healthy", 1, 12, Ct)).TotalCount);
    }

    [Fact]
    public async Task FleetStateFilterSeparatesDueHealthyAndUnknown()
    {
        var template = await Manager.SaveTemplateAsync(null, new TemplateInput("روغن", "روغن", "oil-can", 1000, null, 100, 30, true, 0), Ct);
        async Task<Guid> VehicleWithKm(long? km)
        {
            var vehicle = NewVehicle();
            await Db.SaveChangesAsync();
            await Manager.SaveScheduleAsync(vehicle.Id, null, new ScheduleInput(template.Id, "روغن", 1000, null, 100, 30, 0L, Hour(-10, 8), true, 0), Ct);
            if (km.HasValue) await Manager.SaveOdometerAsync(vehicle.Id, null, new OdometerInput(Guid.NewGuid(), Hour(-1, 8), km.Value, 0, null), Ct);
            return vehicle.Id;
        }
        var due = await VehicleWithKm(2000);
        var healthy = await VehicleWithKm(500);
        var unknown = await VehicleWithKm(null);
        Assert.Equal(due, (await Manager.FleetAsync(null, "due", 1, 12, Ct)).Items.Single().VehicleId);
        Assert.Equal(healthy, (await Manager.FleetAsync(null, "healthy", 1, 12, Ct)).Items.Single().VehicleId);
        Assert.Equal(unknown, (await Manager.FleetAsync(null, "unknown", 1, 12, Ct)).Items.Single().VehicleId);
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

    [Fact]
    public async Task AttachmentValidationRejectsOversizeDisguisedAndMismatchedFiles()
    {
        var (vehicle, template) = await SetupAsync();
        var record = await Manager.SaveRecordAsync(vehicle, null, Input(template), Ct);
        var pdf = "%PDF-1.4\nsynthetic fixture\n%%EOF"u8.ToArray();
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "big.pdf", new byte[5242881], Ct));
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "tiny.pdf", new byte[4], Ct));
        var mz = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00 };
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "evil.pdf", mz, Ct));
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0 };
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "image.jpg", png, Ct));
        var truncatedJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 };
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "photo.jpg", truncatedJpeg, Ct));
        var noEofPdf = "%PDF-1.4\nno eof marker here"u8.ToArray();
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "doc.pdf", noEofPdf, Ct));
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "bad\u0001name.pdf", pdf, Ct));
        var saved = await Manager.AddAttachmentAsync(vehicle, record.Id, "../../invoice.pdf", pdf, Ct);
        Assert.Equal("invoice.pdf", saved.FileName);
        Assert.Single(await Db.MaintenanceAttachments.ToListAsync());
    }

    [Fact]
    public async Task AttachmentCountIsCappedAtTwenty()
    {
        var (vehicle, template) = await SetupAsync();
        var record = await Manager.SaveRecordAsync(vehicle, null, Input(template), Ct);
        var pdf = "%PDF-1.4\nfixture\n%%EOF"u8.ToArray();
        for (var i = 0; i < 20; i++)
            await Manager.AddAttachmentAsync(vehicle, record.Id, $"f{i}.pdf", pdf, Ct);
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "extra.pdf", pdf, Ct));
        Assert.Equal(20, await Db.MaintenanceAttachments.CountAsync(a => a.RecordId == record.Id));
    }

    [Fact]
    public async Task AttachmentRejectedOnVoidedRecord()
    {
        var (vehicle, template) = await SetupAsync();
        var record = await Manager.SaveRecordAsync(vehicle, null, Input(template), Ct);
        await Manager.VoidRecordAsync(vehicle, record.Id, new RevisionInput(record.Version, "ابطال آزمون"), Ct);
        var pdf = "%PDF-1.4\nfixture\n%%EOF"u8.ToArray();
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.AddAttachmentAsync(vehicle, record.Id, "late.pdf", pdf, Ct));
        Assert.Empty(await Db.MaintenanceAttachments.ToListAsync());
    }

    [Fact]
    public async Task LegacyConflictCanBeRepairedInStepsWithoutInventingCurrentMileage()
    {
        var (vehicle, _) = await SetupAsync();
        var first = await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-3, 8), 1000, 0, null), Ct);
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-2, 8), 2000, 0, null), Ct);
        await Db.VehicleOdometerReadings.Where(r => r.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Kilometers, 5000L));
        Db.ChangeTracker.Clear();
        var before = await Manager.OdometerAsync(vehicle, Ct);
        Assert.True(before.HasConflicts);
        Assert.Null(before.CurrentKm);
        var partial = await Manager.SaveOdometerAsync(vehicle, first.Id, new OdometerInput(Guid.NewGuid(), Hour(-3, 8), 3000, 1, "کاهش خطای سابقه"), Ct);
        var intermediate = await Manager.OdometerAsync(vehicle, Ct);
        Assert.True(intermediate.HasConflicts);
        Assert.Equal(before.Readings.Count(r => r.HasConflict), intermediate.Readings.Count(r => r.HasConflict));
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-1, 8), 6000, 0, null), Ct);
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.SaveOdometerAsync(vehicle, first.Id,
            new OdometerInput(Guid.NewGuid(), Hour(-3, 8), 4000, partial.Version, "افزایش ناسازگاری"), Ct));
        Db.ChangeTracker.Clear();
        await Manager.SaveOdometerAsync(vehicle, first.Id, new OdometerInput(Guid.NewGuid(), Hour(-3, 8), 1000, partial.Version, "اصلاح نهایی"), Ct);
        var final = await Manager.OdometerAsync(vehicle, Ct);
        Assert.False(final.HasConflicts);
        Assert.Equal(6000L, final.CurrentKm);
        Assert.Equal(2, (await Manager.RevisionsAsync(vehicle, first.Id, Ct)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VoidedRecordShowsItsLastKnownMileageWithoutResurrectingRemovedReading(bool removeMileage)
    {
        var (vehicle, template) = await SetupAsync();
        var input = Input(template);
        var record = await Manager.SaveRecordAsync(vehicle, null, input, Ct);
        if (removeMileage)
            record = await Manager.SaveRecordAsync(vehicle, record.Id, input with { OdometerKm = null, Version = record.Version, Reason = "کیلومتر نامعلوم" }, Ct);
        await Manager.VoidRecordAsync(vehicle, record.Id, new RevisionInput(record.Version, "ابطال آزمون"), Ct);
        var saved = await Manager.RecordAsync(vehicle, record.Id, Ct);
        Assert.True(saved.IsVoided);
        Assert.Equal(removeMileage ? (long?)null : 1000L, saved.OdometerKm);
        Assert.Null((await Manager.OdometerAsync(vehicle, Ct)).CurrentKm);
    }

    [Fact]
    public async Task DeniedHistoricalMileagePermissionCannotBeBypassedThroughServiceCorrection()
    {
        var (vehicle, template) = await SetupAsync();
        var input = Input(template);
        var record = await Manager.SaveRecordAsync(vehicle, null, input, Ct);
        var limited = new MaintenanceService(Db, new FakeCurrentUser(OtherUsername, OtherUsername,
            EffectivePermissionSet.Build([(PermissionCatalog.Maintenance.All, PermissionEffect.Grant),
                (PermissionCatalog.Maintenance.OdometerCorrect, PermissionEffect.Deny)])), Clock);
        record = await limited.SaveRecordAsync(vehicle, record.Id, input with { OtherCostToman = 75, Version = record.Version, Reason = "فقط هزینه" }, Ct);
        Assert.Equal(775m, record.TotalCostToman);
        var reading = await Db.VehicleOdometerReadings.SingleAsync();
        Assert.Equal(1, reading.Version);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => limited.SaveRecordAsync(vehicle, record.Id,
            input with { OdometerKm = 1100, Version = record.Version, Reason = "تغییر نامجاز" }, Ct));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => limited.SaveRecordAsync(vehicle, record.Id,
            input with { PerformedAtUtc = Hour(-9, 8), Version = record.Version, Reason = "تاریخ نامجاز" }, Ct));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => limited.VoidRecordAsync(vehicle, record.Id, new RevisionInput(record.Version, "ابطال نامجاز"), Ct));
        Assert.False((await Manager.RecordAsync(vehicle, record.Id, Ct)).IsVoided);
        Assert.Single(await Manager.RevisionsAsync(vehicle, record.Id, Ct));
    }

    [Fact]
    public async Task EqualMileageAllowsDistinctVisitsAndRejectsFutureReadings()
    {
        var (vehicle, template) = await SetupAsync();
        await Manager.SaveRecordAsync(vehicle, null, Input(template), Ct);
        await Manager.SaveRecordAsync(vehicle, null, Input(template), Ct);
        Assert.Equal(2, await Db.MaintenanceRecords.CountAsync());
        Assert.False((await Manager.OdometerAsync(vehicle, Ct)).HasConflicts);
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.SaveOdometerAsync(vehicle, null,
            new OdometerInput(Guid.NewGuid(), Clock.UtcNow.AddDays(1), 1000, 0, null), Ct));
    }

    [Fact]
    public async Task SameDayServicesWithIncreasingMileageAreNotFalseConflicts()
    {
        var (vehicle, template) = await SetupAsync();
        // Two services on the same day whose date-only timestamps collide (service time is irrelevant).
        // Increasing mileage is a valid progression, not a simultaneous-reading conflict.
        await Manager.SaveRecordAsync(vehicle, null, Input(template, 1000), Ct);
        await Manager.SaveRecordAsync(vehicle, null, Input(template, 1050), Ct);
        var summary = await Manager.OdometerAsync(vehicle, Ct);
        Assert.False(summary.HasConflicts);
        Assert.Equal(1050L, summary.CurrentKm);
    }

    [Fact]
    public async Task SameDayServiceBelowEarlierReadingStillFlagsBackwardsConflict()
    {
        var (vehicle, template) = await SetupAsync();
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-10, 8), 2000, 0, null), Ct);
        // A later-dated service whose mileage is below the earlier reading is a genuine backwards conflict.
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.SaveRecordAsync(vehicle, null,
            Input(template, 1500) with { PerformedAtUtc = Hour(-5, 8) }, Ct));
    }

    [Theory]
    [InlineData(1403, 6, 31, 1, "1403/07/30")]
    [InlineData(1403, 6, 31, 6, "1403/12/30")]
    [InlineData(1404, 6, 31, 6, "1404/12/29")]
    [InlineData(1403, 12, 30, 12, "1404/12/29")]
    public void JalaliIntervalsClampToActualLastDay(int year, int month, int day, int interval, string expected)
    {
        var date = JalaliDate.StartOfTehranDayUtc(year, month, day).AddHours(13);
        var result = JalaliDate.AddMonths(date, interval);
        Assert.Equal(expected, JalaliDate.FormatDate(result));
        Assert.Equal(13, JalaliDate.ToTehranDateTime(result).Hour);
    }

    [Fact]
    public async Task RemindersRequireBothPermissionsAndSendEachStageOncePerCycle()
    {
        var (vehicle, template) = await SetupAsync();
        var users = await Db.Users.ToListAsync();
        foreach (var account in users)
            Db.UserPermissions.Add(new UserPermission(account.Id, PermissionCatalog.Maintenance.Manage, PermissionEffect.Grant));
        Db.UserPermissions.Add(new UserPermission(users.Single(u => u.Username == RequesterUsername).Id, PermissionCatalog.Maintenance.View, PermissionEffect.Grant));
        Db.UserPermissions.Add(new UserPermission(users.Single(u => u.Username == ApproverUsername).Id, PermissionCatalog.Maintenance.View, PermissionEffect.Deny));
        await Db.SaveChangesAsync();
        var schedule = await Manager.SaveScheduleAsync(vehicle, null, new ScheduleInput(template, "روغن", 1000, null, 100, 30, 0, Hour(-10, 8), true, 0), Ct);
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-3, 8), 900, 0, null), Ct);
        var reminders = new MaintenanceReminders(Db, new PermissionResolver(Db), Clock);
        Assert.Equal(1, await reminders.SendAsync(Ct));
        Assert.Equal(0, await reminders.SendAsync(Ct));
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-2, 8), 1000, 0, null), Ct);
        Assert.Equal(1, await reminders.SendAsync(Ct));
        Assert.Equal(0, await reminders.SendAsync(Ct));
        var record = await Manager.SaveRecordAsync(vehicle, null, Input(template, 1000, schedule.Id) with { PerformedAtUtc = Hour(-1, 8) }, Ct);
        Assert.Equal(0, await reminders.SendAsync(Ct));
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(0, 7), 2000, 0, null), Ct);
        Assert.Equal(1, await reminders.SendAsync(Ct));
        Assert.Equal(0, await reminders.SendAsync(Ct));
        Assert.All(await Db.MaintenanceReminderDeliveries.ToListAsync(), d => Assert.Equal(RequesterUsername, d.Recipient));
        Assert.Equal(3, await Db.UserNotifications.CountAsync(n => n.ReferenceType == "maintenance"));
        await Manager.VoidRecordAsync(vehicle, record.Id, new RevisionInput(record.Version, "آزمون بازگشت دوره"), Ct);
        Assert.Equal(0, await reminders.SendAsync(Ct));
    }

    [Fact]
    public async Task ArchiveSuppressesRemindersAndNewRecords()
    {
        var (vehicle, template) = await SetupAsync();
        var account = await Db.Users.SingleAsync(u => u.Username == RequesterUsername);
        Db.UserPermissions.Add(new UserPermission(account.Id, PermissionCatalog.Maintenance.All, PermissionEffect.Grant));
        await Manager.SaveScheduleAsync(vehicle, null, new ScheduleInput(template, "روغن", 1000, null, 100, 30, 0, Hour(-10, 8), true, 0), Ct);
        await Manager.SaveOdometerAsync(vehicle, null, new OdometerInput(Guid.NewGuid(), Hour(-1, 8), 1000, 0, null), Ct);
        (await Db.Vehicles.SingleAsync(v => v.Id == vehicle)).Archive();
        await Db.SaveChangesAsync();
        Assert.Equal(0, await new MaintenanceReminders(Db, new PermissionResolver(Db), Clock).SendAsync(Ct));
        await Assert.ThrowsAsync<ValidationAppException>(() => Manager.SaveRecordAsync(vehicle, null, Input(template), Ct));
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

    [Theory]
    [InlineData("030-tools")]
    [InlineData("100-dashboard-interface")]
    [InlineData("055-manual-transmission")]
    [InlineData("general-service")]
    [InlineData("oil-can")]
    [InlineData("engine")]
    public void AssetKeyAcceptsPackAndLegacySlugs(string key) => Assert.True(MaintenanceRules.IsValidAssetKey(key));

    [Theory]
    [InlineData("")]
    [InlineData("../secret")]
    [InlineData("a/b")]
    [InlineData("Oil-Can")]
    [InlineData("oil can")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    public void AssetKeyRejectsTraversalAndMalformedSlugs(string key) => Assert.False(MaintenanceRules.IsValidAssetKey(key));

    [Fact]
    public void AssetKeyRejectsOverColumnWidth() => Assert.False(MaintenanceRules.IsValidAssetKey(new string('a', 41)));
}

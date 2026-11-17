using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Vehicles;

namespace NovaFleet.UnitTests.Missions;

/// <summary>
/// ماشین حالت چرخه عمر مأموریت در MissionService: از ثبت درخواست تا بررسی، تأیید، اجرا، خاتمه و انصراف.
/// </summary>
public sealed class MissionLifecycleTests : MissionTestBase
{
    [Theory]
    [InlineData(MissionStatus.Pending)]
    [InlineData(MissionStatus.UnderReview)]
    [InlineData(MissionStatus.Rejected)]
    [InlineData(MissionStatus.Cancelled)]
    public async Task ExportOrderPdfAsync_RejectsMissionWithoutApproval(MissionStatus status)
    {
        var created = await CreatePendingAsync();
        var service = ServiceFor(MissionAdmin());
        if (status == MissionStatus.UnderReview)
        {
            await service.StartReviewAsync(created.Id);
        }
        else if (status == MissionStatus.Rejected)
        {
            await service.RejectAsync(created.Id, new MissionRejectionRequest("رد درخواست"));
        }
        else if (status == MissionStatus.Cancelled)
        {
            await service.CancelAsync(created.Id, new MissionCancelRequest("لغو پیش از تأیید"));
        }

        Assert.Null(Reload(created.Id).ApprovedAtUtc);
        var printer = ServiceFor(User(RequesterUsername, PermissionCatalog.Missions.Print));
        var error = await Assert.ThrowsAsync<ValidationAppException>(() => printer.ExportOrderPdfAsync(created.Id));
        Assert.Contains("پس از تأیید", error.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExportOrderPdfAsync_RejectsWithdrawalPendingOrCancelledAfterApproval(bool running, bool cancel)
    {
        var confirmed = await ConfirmAsync();
        if (running)
        {
            await MarkRunningAsync(confirmed.Id, Hour(0, 10));
        }

        var service = ServiceFor(MissionAdmin());
        await service.RequestWithdrawalAsync(confirmed.Id, new MissionWithdrawalRequest("انصراف"));
        if (cancel)
        {
            await service.DecideWithdrawalAsync(confirmed.Id, true, new MissionWithdrawalDecision("تأیید انصراف"));
        }

        var mission = Reload(confirmed.Id);
        Assert.NotNull(mission.ApprovedAtUtc);
        Assert.Equal(cancel ? MissionStatus.Cancelled : MissionStatus.WithdrawalPending, mission.Status);
        var printer = ServiceFor(User(RequesterUsername, PermissionCatalog.Missions.Print));
        await Assert.ThrowsAsync<ValidationAppException>(() => printer.ExportOrderPdfAsync(confirmed.Id));
    }

    [Fact]
    public async Task ExportOrderPdfAsync_RequiresPrintPermissionAfterApproval()
    {
        var confirmed = await ConfirmAsync();

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            ServiceFor(User(RequesterUsername)).ExportOrderPdfAsync(confirmed.Id));
    }

    [Fact]
    public async Task CreateAsync_StoresPendingMissionWithJalaliNumber()
    {
        var mission = await CreatePendingAsync();

        Assert.Equal(MissionStatus.Pending, mission.Status);
        Assert.Matches(@"^1405-\d{4}$", mission.MissionNumber);
        Assert.Equal(RequesterUsername, mission.RequesterUsername);
        Assert.Equal("واحد ترانسفر", mission.RequesterOrganizationalUnit);
        Assert.Equal(MissionStatus.Pending, Reload(mission.Id).Status);
        Assert.True(Audit.Wrote("CreateMission"));
    }

    [Fact]
    public async Task CreateAsync_NumbersMissionsSequentiallyInSameYear()
    {
        var first = await CreatePendingAsync();
        var second = await CreatePendingAsync();

        Assert.Equal("1405-0001", first.MissionNumber);
        Assert.Equal("1405-0002", second.MissionNumber);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-800)]
    public async Task CreateAsync_AllowsUnlimitedPastAndStoresPendingBadge(int day)
    {
        var service = ServiceFor(MissionAdmin());
        var created = await service.CreateAsync(Request(Hour(day, 6), Hour(day, 9)));

        Assert.Equal(MissionStatus.Pending, created.Status);
        Assert.True(created.IsBackdated);
        Assert.True(Reload(created.Id).IsBackdated);
        Assert.Null(created.ApprovedAtUtc);
        Assert.Null(created.ActualEndAtUtc);
        var list = await service.SearchAsync(new MissionQuery(null, null, null, null, null, null, null, null, false, 1, 20));
        Assert.True(Assert.Single(list.Items).IsBackdated);
    }

    [Theory]
    [InlineData(-1, 0, true)]
    [InlineData(-1, 23, true)]
    [InlineData(-2, 23, false)]
    public async Task CreateAsync_UsesInclusiveTehranCalendarDayLimit(int day, int hour, bool allowed)
    {
        Settings.Values[SettingKeys.BackdateRequestDays] = "1";
        var start = Hour(day, hour);
        var service = ServiceFor(MissionAdmin());
        if (allowed)
        {
            var created = await service.CreateAsync(Request(start, start.AddHours(1)));
            Assert.True(created.IsBackdated);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ValidationAppException>(() => service.CreateAsync(Request(start, start.AddHours(1))));
            Assert.Contains("1 روز گذشته", error.Message);
            Assert.Empty(Db.Missions);
        }
    }

    [Fact]
    public async Task CreateAsync_AllowsStartedButNotYetEndedMission()
    {
        var created = await ServiceFor(MissionAdmin()).CreateAsync(Request(Hour(0, 6), Hour(0, 12)));
        Assert.True(created.IsBackdated);
        Assert.Equal(MissionStatus.Pending, created.Status);
    }

    [Fact]
    public async Task CreateAsync_FutureMissionDoesNotAcquireBadgeAsTimePasses()
    {
        var created = await CreatePendingAsync();
        Clock.NowUtc = Hour(2, 8);
        var service = ServiceFor(MissionAdmin());
        var details = await service.GetAsync(created.Id);
        Assert.False(details.IsBackdated);
        var edited = await service.UpdatePendingAsync(created.Id, ValidRequest() with { Title = "عنوان اصلاح‌شده" });
        Assert.False(edited.IsBackdated);
    }

    [Fact]
    public async Task UpdatePendingAsync_RecomputesBadgeWhenDatesChangeAndEnforcesLimit()
    {
        var created = await CreatePendingAsync();
        var service = ServiceFor(MissionAdmin());
        var past = await service.UpdatePendingAsync(created.Id, Request(Hour(-1, 6), Hour(-1, 9)));
        Assert.True(past.IsBackdated);
        Assert.Equal(MissionStatus.Pending, past.Status);
        Assert.True(Reload(created.Id).IsBackdated);
        Settings.Values[SettingKeys.BackdateRequestDays] = "1";
        await Assert.ThrowsAsync<ValidationAppException>(() => service.UpdatePendingAsync(created.Id, Request(Hour(-2, 6), Hour(-2, 9))));
        Assert.Equal(Hour(-1, 6), Reload(created.Id).PlannedStartAtUtc);
        var future = await service.UpdatePendingAsync(created.Id, ValidRequest());
        Assert.False(future.IsBackdated);
    }

    [Fact]
    public async Task BackdatedMission_RequiresApprovalBeforeAutomaticLifecycle()
    {
        var requester = ServiceFor(User(RequesterUsername, PermissionCatalog.Missions.Create));
        var created = await requester.CreateAsync(Request(Hour(-1, 6), Hour(-1, 9)));
        var (driver, vehicle) = SaveReadyResources();
        await Assert.ThrowsAsync<ForbiddenAppException>(() => requester.ApproveAsync(created.Id, new MissionApprovalRequest(driver.Id, vehicle.Id, null)));
        var entity = await Db.Missions.SingleAsync(m => m.Id == created.Id);
        Assert.Throws<InvalidOperationException>(() => entity.StartAutomatically(Clock.UtcNow));
        var approved = await ServiceFor(User(ApproverUsername, PermissionCatalog.Missions.Approve))
            .ApproveAsync(created.Id, new MissionApprovalRequest(driver.Id, vehicle.Id, null));
        Assert.Equal(MissionStatus.Confirmed, approved.Status);
        Assert.True(approved.IsBackdated);
        Assert.Equal(ApproverUsername, approved.ApprovedBy);
        Assert.False(Sms.QueuedFor(created.Id, SmsEventType.MissionStartReminder));
        entity.StartAutomatically(Clock.UtcNow);
        entity.CompleteAutomatically(Clock.UtcNow);
        await Db.SaveChangesAsync();
        var completed = Reload(created.Id);
        Assert.Equal(MissionStatus.Completed, completed.Status);
        Assert.True(completed.IsBackdated);
        Assert.Equal(Hour(-1, 6), completed.ActualStartAtUtc);
        Assert.Equal(Hour(-1, 9), completed.ActualEndAtUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApproveAsync_RejectsHistoricalOverlapWithCompletedDriverOrVehicle(bool sameDriver)
    {
        var resources = SaveReadyResources();
        var prior = await ConfirmAsync(resources, Hour(-1, 10), Hour(-1, 14));
        var entity = await Db.Missions.SingleAsync(m => m.Id == prior.Id);
        entity.StartAutomatically(Clock.UtcNow);
        entity.CompleteAutomatically(Clock.UtcNow);
        await Db.SaveChangesAsync();
        var other = SaveReadyResources();
        var service = ServiceFor(MissionAdmin());
        var created = await service.CreateAsync(Request(Hour(-1, 11), Hour(-1, 13)));
        await Assert.ThrowsAsync<ConflictAppException>(() => service.ApproveAsync(created.Id, new MissionApprovalRequest(
            sameDriver ? resources.Driver.Id : other.Driver.Id,
            sameDriver ? other.Vehicle.Id : resources.Vehicle.Id, null)));
        Assert.Equal(MissionStatus.Pending, Reload(created.Id).Status);
    }

    [Fact]
    public async Task CreateAsync_RejectsStartBeyondFutureWindow()
    {
        var service = ServiceFor(MissionAdmin());

        var error = await Assert.ThrowsAsync<ValidationAppException>(
            () => service.CreateAsync(Request(Hour(40, 10), Hour(40, 14))));
        Assert.Contains(SettingKeys.DefaultFutureRequestDays.ToString(), error.Message);
    }

    [Fact]
    public async Task CreateAsync_UsesConfiguredFutureWindow()
    {
        Settings.Values[SettingKeys.FutureRequestDays] = "5";
        var service = ServiceFor(MissionAdmin());

        await Assert.ThrowsAsync<ValidationAppException>(
            () => service.CreateAsync(Request(Hour(8, 10), Hour(8, 14))));
    }

    [Fact]
    public async Task CreateAsync_RejectsTimeThatIsNotAFullTehranHour()
    {
        var service = ServiceFor(MissionAdmin());

        await Assert.ThrowsAsync<ValidationAppException>(
            () => service.CreateAsync(Request(Hour(0, 10).AddMinutes(30), Hour(0, 14))));
    }

    [Fact]
    public async Task CreateAsync_RejectsEndNotAfterStart()
    {
        var service = ServiceFor(MissionAdmin());

        await Assert.ThrowsAsync<ValidationAppException>(
            () => service.CreateAsync(Request(Hour(0, 14), Hour(0, 10))));
    }

    [Fact]
    public async Task CreateAsync_AllowsRequesterWithoutOrganizationalUnit()
    {
        var service = ServiceFor(User("demo.outsider", PermissionCatalog.Missions.Create));

        var mission = await service.CreateAsync(Request(Hour(0, 10), Hour(0, 14)));

        Assert.Equal(MissionStatus.Pending, mission.Status);
        Assert.Equal(string.Empty, mission.RequesterOrganizationalUnit);
    }

    [Fact]
    public async Task CreateAsync_TakesOrganizationalUnitFromProfileWhenFormOmitsIt()
    {
        var mission = await CreatePendingAsync();

        Assert.Equal("واحد ترانسفر", mission.RequesterOrganizationalUnit);
    }

    [Fact]
    public async Task CreateAsync_AllowsMissingPurpose()
    {
        var service = ServiceFor(MissionAdmin());

        var mission = await service.CreateAsync(ValidRequest() with { Purpose = null });

        Assert.Null(mission.Purpose);
    }

    [Fact]
    public async Task CreateAsync_TakesPhoneFromProfileWhenFormOmitsIt()
    {
        var requester = await Db.Users.FirstAsync(item => item.Username == RequesterUsername);
        requester.UpdateProfile(requester.FirstName, requester.LastName, "09120000000", requester.Email, requester.OrganizationalUnit);
        await Db.SaveChangesAsync();
        var service = ServiceFor(MissionAdmin());

        var mission = await service.CreateAsync(ValidRequest() with { RequesterPhoneSnapshot = "" });

        Assert.Equal("09120000000", mission.RequesterPhoneSnapshot);
    }

    [Fact]
    public async Task CreateAsync_WithoutCreatePermissionIsForbidden()
    {
        var service = ServiceFor(User(RequesterUsername, PermissionCatalog.Missions.View));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.CreateAsync(ValidRequest()));
    }

    [Fact]
    public async Task StartReviewAsync_MovesPendingToUnderReviewAndNotifiesRequester()
    {
        var created = await CreatePendingAsync();
        var reviewer = User(ApproverUsername, PermissionCatalog.Missions.Review);

        var reviewed = await ServiceFor(reviewer).StartReviewAsync(created.Id);

        Assert.Equal(MissionStatus.UnderReview, reviewed.Status);
        Assert.NotNull(reviewed.ReviewStartedAtUtc);
        Assert.Equal("mission.review.started", Notifications.LastEventKeyFor(RequesterUsername));
    }

    [Fact]
    public async Task StartReviewAsync_WithoutReviewPermissionIsForbidden()
    {
        var created = await CreatePendingAsync();
        var service = ServiceFor(User(ApproverUsername, PermissionCatalog.Missions.Approve));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.StartReviewAsync(created.Id));
    }

    [Fact]
    public async Task StartReviewAsync_OnAlreadyConfirmedMissionIsConflict()
    {
        var confirmed = await ConfirmAsync();

        await Assert.ThrowsAsync<ConflictAppException>(
            () => ServiceFor(MissionAdmin()).StartReviewAsync(confirmed.Id));
    }

    [Fact]
    public async Task ApproveAsync_FromPendingDirectlyConfirmsAndAssignsResources()
    {
        var created = await CreatePendingAsync();
        var (driver, vehicle) = SaveReadyResources();

        var approved = await ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id, new MissionApprovalRequest(driver.Id, vehicle.Id, null));

        Assert.Equal(MissionStatus.Confirmed, approved.Status);
        Assert.Equal(driver.Id, approved.DriverId);
        Assert.Equal(vehicle.Id, approved.VehicleId);
        Assert.Equal(RequesterUsername, Reload(created.Id).ApprovedBy);
    }

    [Fact]
    public async Task ApproveAsync_QueuesApprovalAndStartReminderSms()
    {
        var created = await CreatePendingAsync();
        var (driver, vehicle) = SaveReadyResources();

        await ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id, new MissionApprovalRequest(driver.Id, vehicle.Id, null));

        Assert.True(Sms.QueuedFor(created.Id, SmsEventType.MissionApproved));
        Assert.True(Sms.QueuedFor(created.Id, SmsEventType.MissionStartReminder));
    }

    [Fact]
    public async Task ApproveAsync_WithUnknownDriverIsNotFound()
    {
        var created = await CreatePendingAsync();
        var vehicle = NewVehicle();
        Db.SaveChanges();

        await Assert.ThrowsAsync<NotFoundAppException>(() => ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id, new MissionApprovalRequest(Guid.NewGuid(), vehicle.Id, null)));
    }

    [Fact]
    public async Task ApproveAsync_WithArchivedDriverIsConflict()
    {
        var created = await CreatePendingAsync();
        var driver = NewDriver();
        var vehicle = NewVehicle();
        driver.Archive();
        Db.SaveChanges();

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id, new MissionApprovalRequest(driver.Id, vehicle.Id, null)));
        Assert.Contains("راننده", error.Message);
    }

    [Fact]
    public async Task ApproveAsync_WithVehicleInRepairShopIsConflict()
    {
        var created = await CreatePendingAsync();
        var driver = NewDriver();
        var vehicle = NewVehicle();
        vehicle.ChangeManualStatus(ManualVehicleStatus.RepairShop, hasActiveMission: false);
        Db.SaveChanges();

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id, new MissionApprovalRequest(driver.Id, vehicle.Id, null)));
        Assert.Contains("خودرو", error.Message);
    }

    [Fact]
    public async Task ApproveAsync_WithOverlappingResourcesIsConflict()
    {
        var resources = SaveReadyResources();
        await ConfirmAsync(resources, Hour(0, 10), Hour(0, 14));
        var overlapping = await ServiceFor(MissionAdmin()).CreateAsync(Request(Hour(0, 12), Hour(0, 16)));

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin()).ApproveAsync(
            overlapping.Id, new MissionApprovalRequest(resources.Driver.Id, resources.Vehicle.Id, null)));
        Assert.Contains("تداخل", error.Message);
    }

    [Fact]
    public async Task ApproveAsync_AllowsSeparationWiderThanMinimumGap()
    {
        var resources = SaveReadyResources();
        await ConfirmAsync(resources, Hour(0, 10), Hour(0, 14));
        var later = await ServiceFor(MissionAdmin()).CreateAsync(Request(Hour(0, 15), Hour(0, 17)));

        var approved = await ServiceFor(MissionAdmin()).ApproveAsync(
            later.Id, new MissionApprovalRequest(resources.Driver.Id, resources.Vehicle.Id, null));

        Assert.Equal(MissionStatus.Confirmed, approved.Status);
    }

    [Fact]
    public async Task RejectAsync_FromPendingStoresReason()
    {
        var created = await CreatePendingAsync();

        var rejected = await ServiceFor(MissionAdmin())
            .RejectAsync(created.Id, new MissionRejectionRequest("عدم امکان تخصیص خودرو"));

        Assert.Equal(MissionStatus.Rejected, rejected.Status);
        Assert.Equal("عدم امکان تخصیص خودرو", rejected.DecisionReason);
        Assert.True(Sms.QueuedFor(created.Id, SmsEventType.MissionRejected));
    }

    [Fact]
    public async Task RejectAsync_OnConfirmedMissionIsConflict()
    {
        var confirmed = await ConfirmAsync();

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .RejectAsync(confirmed.Id, new MissionRejectionRequest("دیره")));
    }

    [Fact]
    public async Task CancelAsync_OnConfirmedMissionSetsCancelledAndStopsReminder()
    {
        var confirmed = await ConfirmAsync();

        var cancelled = await ServiceFor(MissionAdmin())
            .CancelAsync(confirmed.Id, new MissionCancelRequest("انصراف درخواست‌کننده"));

        Assert.Equal(MissionStatus.Cancelled, cancelled.Status);
        Assert.Contains(confirmed.Id, Sms.CancelledReminders);
    }

    [Fact]
    public async Task CancelAsync_OnCompletedMissionIsConflict()
    {
        var completed = await CompleteRunningAsync();

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .CancelAsync(completed.Id, new MissionCancelRequest("تکراری")));
    }

    [Fact]
    public async Task ExtendAsync_OnConfirmedMissionMovesPlannedEnd()
    {
        var confirmed = await ConfirmAsync(plannedEnd: Hour(0, 14));

        var extended = await ServiceFor(MissionAdmin())
            .ExtendAsync(confirmed.Id, new MissionExtensionRequest(Hour(0, 16), "تأخیر در بارگیری"));

        Assert.Equal(Hour(0, 16), extended.PlannedEndAtUtc);
        Assert.Equal(MissionStatus.Confirmed, extended.Status);
        Assert.True(Sms.QueuedFor(confirmed.Id, SmsEventType.MissionChanged));
    }

    [Fact]
    public async Task ExtendAsync_ToEarlierTimeIsValidationFailure()
    {
        var confirmed = await ConfirmAsync(plannedEnd: Hour(0, 14));

        await Assert.ThrowsAsync<ValidationAppException>(() => ServiceFor(MissionAdmin())
            .ExtendAsync(confirmed.Id, new MissionExtensionRequest(Hour(0, 12), "کوتاه‌سازی")));
    }

    [Fact]
    public async Task ExtendAsync_OnPendingMissionIsConflict()
    {
        var created = await CreatePendingAsync();

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .ExtendAsync(created.Id, new MissionExtensionRequest(Hour(0, 20), "تمدید")));
    }

    [Fact]
    public async Task ExtendAsync_WhenNewEndReachesNextMissionIsConflict()
    {
        var resources = SaveReadyResources();
        var first = await ConfirmAsync(resources, Hour(0, 10), Hour(0, 14));
        await ConfirmAsync(resources, Hour(1, 10), Hour(1, 14));

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .ExtendAsync(first.Id, new MissionExtensionRequest(Hour(1, 12), "همپوشانی")));
        Assert.Contains("تمدید", error.Message);
    }

    [Fact]
    public async Task EndAsync_OnRunningMissionCompletesAndComputesDistance()
    {
        var confirmed = await ConfirmAsync(plannedStart: Hour(0, 10), plannedEnd: Hour(0, 14));
        await MarkRunningAsync(confirmed.Id, Hour(0, 10));
        Clock.NowUtc = Hour(0, 12);

        var ended = await ServiceFor(MissionAdmin())
            .EndAsync(confirmed.Id, new MissionEndRequest(Hour(0, 12), 1_050, "تحویل بار"));

        Assert.Equal(MissionStatus.Completed, ended.Status);
        Assert.Equal(Hour(0, 12), ended.ActualEndAtUtc);
        Assert.Equal(50L, ended.DistanceKm);
        Assert.True(Sms.QueuedFor(confirmed.Id, SmsEventType.MissionCompleted));
    }

    [Fact]
    public async Task EndAsync_OnConfirmedMissionBeforeStartIsConflict()
    {
        var confirmed = await ConfirmAsync();

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .EndAsync(confirmed.Id, new MissionEndRequest(Hour(0, 11), null, "زودهنگام")));
    }

    [Fact]
    public async Task EndAsync_WithoutApproveOrExtendPermissionIsForbidden()
    {
        var confirmed = await ConfirmAsync();
        await MarkRunningAsync(confirmed.Id, Hour(0, 10));
        var service = ServiceFor(User(OtherUsername, PermissionCatalog.Missions.View));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service
            .EndAsync(confirmed.Id, new MissionEndRequest(Hour(0, 11), null, "بدون مجوز")));
    }

    [Fact]
    public async Task RequestWithdrawalAsync_OnConfirmedMissionRemembersPreviousStatus()
    {
        var confirmed = await ConfirmAsync();

        var pending = await ServiceFor(MissionAdmin())
            .RequestWithdrawalAsync(confirmed.Id, new MissionWithdrawalRequest("مرخصی راننده"));

        Assert.Equal(MissionStatus.WithdrawalPending, pending.Status);
        Assert.Equal(MissionStatus.Confirmed, pending.StatusBeforeWithdrawal);
    }

    [Fact]
    public async Task RequestWithdrawalAsync_OnPendingMissionIsConflict()
    {
        var created = await CreatePendingAsync();

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .RequestWithdrawalAsync(created.Id, new MissionWithdrawalRequest("زودتر از موعد")));
    }

    [Fact]
    public async Task DecideWithdrawalAsync_ApproveCancelsTheMission()
    {
        var confirmed = await ConfirmAsync();
        await ServiceFor(MissionAdmin()).RequestWithdrawalAsync(
            confirmed.Id, new MissionWithdrawalRequest("انصراف"));

        var decided = await ServiceFor(MissionAdmin())
            .DecideWithdrawalAsync(confirmed.Id, true, new MissionWithdrawalDecision("تأیید انصراف"));

        Assert.Equal(MissionStatus.Cancelled, decided.Status);
        Assert.Null(decided.StatusBeforeWithdrawal);
        Assert.True(Sms.QueuedFor(confirmed.Id, SmsEventType.WithdrawalApproved));
    }

    [Fact]
    public async Task DecideWithdrawalAsync_RejectReturnsToPreviousStatus()
    {
        var confirmed = await ConfirmAsync();
        await ServiceFor(MissionAdmin()).RequestWithdrawalAsync(
            confirmed.Id, new MissionWithdrawalRequest("انصراف"));

        var decided = await ServiceFor(MissionAdmin())
            .DecideWithdrawalAsync(confirmed.Id, false, new MissionWithdrawalDecision("نیاز به مأموریت"));

        Assert.Equal(MissionStatus.Confirmed, decided.Status);
        Assert.True(Sms.QueuedFor(confirmed.Id, SmsEventType.WithdrawalRejected));
    }

    [Fact]
    public async Task DecideWithdrawalAsync_WithoutReviewPermissionIsForbidden()
    {
        var confirmed = await ConfirmAsync();
        await ServiceFor(MissionAdmin()).RequestWithdrawalAsync(
            confirmed.Id, new MissionWithdrawalRequest("انصراف"));
        var service = ServiceFor(User(OtherUsername, PermissionCatalog.Missions.View));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service
            .DecideWithdrawalAsync(confirmed.Id, true, new MissionWithdrawalDecision(null)));
    }

    /// <summary>مأموریت تا Completed می‌رود تا لغوی دوباره بر آن سنجیده شود.</summary>
    private async Task<MissionDetailsDto> CompleteRunningAsync()
    {
        var confirmed = await ConfirmAsync(plannedStart: Hour(0, 10), plannedEnd: Hour(0, 14));
        await MarkRunningAsync(confirmed.Id, Hour(0, 10));
        Clock.NowUtc = Hour(0, 12);
        return await ServiceFor(MissionAdmin())
            .EndAsync(confirmed.Id, new MissionEndRequest(Hour(0, 12), 1_050, "پایان آزمون"));
    }
}

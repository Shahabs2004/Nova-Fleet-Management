using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;

namespace NovaFleet.UnitTests.Missions;

/// <summary>
/// درخواست تغییر پس از تأیید و تقسیم مأموریت در حال اجرا به دو بخش.
/// </summary>
public sealed class MissionChangeAndSplitTests : MissionTestBase
{
    [Fact]
    public async Task RequestChangeAsync_OnConfirmedMissionStoresPendingChange()
    {
        var confirmed = await ConfirmAsync();
        var proposed = ProposedChange(confirmed, Hour(0, 11), Hour(0, 15));

        var details = await ServiceFor(MissionAdmin())
            .RequestChangeAsync(confirmed.Id, new MissionChangeRequestInput("تغییر ساعت بارگیری", proposed));

        var change = Assert.Single(details.ChangeRequests);
        Assert.Equal(MissionChangeRequestStatus.Pending, change.Status);
        Assert.Equal(RequesterUsername, change.RequesterUsername);
        Assert.Equal(MissionStatus.Confirmed, Reload(confirmed.Id).Status);
        Assert.Equal("mission.change.requested", Notifications.LastEventKeyFor("role:" + PermissionCatalog.Missions.ChangeReview));
    }

    [Fact]
    public async Task RequestChangeAsync_OnPendingMissionIsConflict()
    {
        var created = await CreatePendingAsync();
        var details = await ServiceFor(MissionAdmin()).GetAsync(created.Id);

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin()).RequestChangeAsync(
            created.Id,
            new MissionChangeRequestInput("زودهنگام", ProposedChange(details, Hour(0, 11), Hour(0, 15)))));
    }

    [Fact]
    public async Task RequestChangeAsync_WithoutProposedDriverIsValidationFailure()
    {
        var confirmed = await ConfirmAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() => ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "حذف راننده",
                ProposedChange(confirmed, Hour(0, 11), Hour(0, 15), driverId: Guid.Empty))));
    }

    [Fact]
    public async Task RequestChangeAsync_ByAnotherUserWithoutChangeReviewIsForbidden()
    {
        var confirmed = await ConfirmAsync();
        var outsider = User(OtherUsername, PermissionCatalog.Missions.Edit);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => ServiceFor(outsider).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput("تغییر متفرقه", ProposedChange(confirmed, Hour(0, 11), Hour(0, 15)))));
    }

    [Fact]
    public async Task ReviewChangeAsync_MarksChangeUnderReviewAndRecordsReviewer()
    {
        var (missionId, changeId) = await RequestedChangeAsync();

        var details = await ServiceFor(User(ApproverUsername, PermissionCatalog.Missions.ChangeReview))
            .ReviewChangeAsync(missionId, changeId, start: true);

        var change = details.ChangeRequests.Single(item => item.Id == changeId);
        Assert.Equal(MissionChangeRequestStatus.UnderReview, change.Status);
        Assert.Equal(ApproverUsername, change.ReviewerUsername);
    }

    [Fact]
    public async Task ReviewChangeAsync_WithoutChangeReviewPermissionIsForbidden()
    {
        var (missionId, changeId) = await RequestedChangeAsync();
        var service = ServiceFor(User(ApproverUsername, PermissionCatalog.Missions.Approve));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.ReviewChangeAsync(missionId, changeId, start: true));
    }

    [Fact]
    public async Task DecideChangeAsync_RejectLeavesMissionUntouched()
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var before = Reload(missionId);

        var details = await ServiceFor(User(ApproverUsername, PermissionCatalog.Missions.ChangeReview))
            .DecideChangeAsync(missionId, changeId, approve: false, new MissionChangeDecision("تداخل با ناوگان"));

        var change = details.ChangeRequests.Single(item => item.Id == changeId);
        Assert.Equal(MissionChangeRequestStatus.Rejected, change.Status);
        Assert.Equal("تداخل با ناوگان", change.DecisionReason);
        Assert.Equal(before.PlannedStartAtUtc, Reload(missionId).PlannedStartAtUtc);
    }

    [Fact]
    public async Task DecideChangeAsync_ApproveAppliesNewTimesAndKeepsResources()
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var mission = Reload(missionId);

        await ServiceFor(User(ApproverUsername, PermissionCatalog.Missions.ChangeReview))
            .DecideChangeAsync(missionId, changeId, approve: true, new MissionChangeDecision("موافقت"));

        var after = Reload(missionId);
        Assert.Equal(Hour(0, 11), after.PlannedStartAtUtc);
        Assert.Equal(Hour(0, 15), after.PlannedEndAtUtc);
        Assert.Equal(mission.DriverId, after.DriverId);
        Assert.Equal(MissionChangeRequestStatus.Approved, ReloadChange(changeId).Status);
    }

    [Fact]
    public async Task DecideChangeAsync_WhenStartMovesReschedulesStartReminder()
    {
        var (missionId, changeId) = await StartedChangeAsync();

        await ServiceFor(User(ApproverUsername, PermissionCatalog.Missions.ChangeReview))
            .DecideChangeAsync(missionId, changeId, approve: true, new MissionChangeDecision("موافقت"));

        Assert.Contains(missionId, Sms.RescheduledReminders);
    }

    [Fact]
    public async Task DecideChangeAsync_WhenOnlyTimesChangeQueuesMissionChangedSms()
    {
        var (missionId, changeId) = await StartedChangeAsync();

        await ServiceFor(User(ApproverUsername, PermissionCatalog.Missions.ChangeReview))
            .DecideChangeAsync(missionId, changeId, approve: true, new MissionChangeDecision("موافقت"));

        Assert.True(Sms.QueuedFor(missionId, SmsEventType.MissionChanged));
        Assert.False(Sms.QueuedFor(missionId, SmsEventType.DriverOrVehicleChanged));
    }

    [Fact]
    public async Task DecideChangeAsync_ByAnotherReviewerIsConflict()
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var otherReviewer = User("demo.secondreviewer", PermissionCatalog.Missions.ChangeReview);

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(otherReviewer)
            .DecideChangeAsync(missionId, changeId, approve: true, new MissionChangeDecision("موافقت")));
    }

    [Fact]
    public async Task DecideChangeAsync_WithoutStartedReviewIsConflict()
    {
        var (missionId, changeId) = await RequestedChangeAsync();

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .DecideChangeAsync(missionId, changeId, approve: true, new MissionChangeDecision("موافقت")));
    }

    [Fact]
    public async Task DecideChangeAsync_WithNewVehicleButNoOdometerIsConflict()
    {
        var confirmed = await ConfirmAsync();
        var replacement = NewVehicle();
        Db.SaveChanges();
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تعویض خودرو",
                ProposedChange(confirmed, Hour(0, 10), Hour(0, 14), vehicleId: replacement.Id)));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(MissionAdmin()).ReviewChangeAsync(confirmed.Id, changeId, start: true);

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .DecideChangeAsync(confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت")));
    }

    [Fact]
    public async Task DecideChangeAsync_WithNewVehicleAndOdometerReassignsResources()
    {
        var confirmed = await ConfirmAsync();
        var replacement = NewVehicle();
        Db.SaveChanges();
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تعویض خودرو",
                ProposedChange(confirmed, Hour(0, 10), Hour(0, 14), vehicleId: replacement.Id, odometerStartKm: 5_500)));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(MissionAdmin()).ReviewChangeAsync(confirmed.Id, changeId, start: true);

        await ServiceFor(MissionAdmin())
            .DecideChangeAsync(confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت"));

        var after = Reload(confirmed.Id);
        Assert.Equal(replacement.Id, after.VehicleId);
        Assert.Equal(5_500L, after.OdometerStartKm);
        Assert.Null(after.OdometerEndKm);
        Assert.True(Sms.QueuedFor(confirmed.Id, SmsEventType.DriverOrVehicleChanged));
    }

    [Fact]
    public async Task DecideChangeAsync_WhenProposedTimeCollidesWithOtherMissionIsConflict()
    {
        var resources = SaveReadyResources();
        await ConfirmAsync(resources, Hour(0, 10), Hour(0, 14));
        var second = await ConfirmAsync(resources, Hour(1, 10), Hour(1, 14));
        var changeId = (await ServiceFor(MissionAdmin()).RequestChangeAsync(
            second.Id,
            new MissionChangeRequestInput(
                "انتقال به روز اول",
                ProposedChange(second, Hour(0, 12), Hour(0, 16), resources.Driver.Id, resources.Vehicle.Id))))
            .ChangeRequests.Single().Id;
        await ServiceFor(MissionAdmin()).ReviewChangeAsync(second.Id, changeId, start: true);

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin())
            .DecideChangeAsync(second.Id, changeId, approve: true, new MissionChangeDecision("موافقت")));
        Assert.Contains("تداخل", error.Message);
        Assert.Equal(Hour(1, 10), Reload(second.Id).PlannedStartAtUtc);
    }

    [Fact]
    public async Task DecideChangeAsync_WithUnreadableProposedPayloadIsValidationFailure()
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var change = await Db.MissionChangeRequests.FirstAsync(item => item.Id == changeId);
        Db.Entry(change).Property(nameof(MissionChangeRequest.ProposedValuesJson)).CurrentValue = "null";
        await Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() => ServiceFor(MissionAdmin())
            .DecideChangeAsync(missionId, changeId, approve: true, new MissionChangeDecision("موافقت")));
    }

    [Fact]
    public async Task SplitAsync_OnRunningMissionClosesFirstSegmentAtSplitTime()
    {
        var running = await ConfirmRunningAsync();

        var first = await ServiceFor(MissionAdmin()).SplitAsync(
            running.Id, new MissionSplitRequest(Hour(0, 12), 1_100, null, null, "تغییر راننده در میانه مسیر", null));

        Assert.Equal(MissionStatus.Completed, first.Status);
        Assert.Equal(Hour(0, 12), first.PlannedEndAtUtc);
        Assert.Equal(Hour(0, 12), first.ActualEndAtUtc);
        Assert.Equal(1_100L, first.OdometerEndKm);
        Assert.Equal(100L, first.DistanceKm);
        Assert.True(Audit.Wrote("SplitMission"));
    }

    [Fact]
    public async Task SplitAsync_CreatesSecondLegWithNewNumberAndPendingStatus()
    {
        var running = await ConfirmRunningAsync();

        var first = await ServiceFor(MissionAdmin()).SplitAsync(
            running.Id, new MissionSplitRequest(Hour(0, 12), 1_100, null, null, "تغییر راننده", null));

        var second = Db.Missions.AsNoTracking().Single(item => item.Id != first.Id);
        Assert.Equal("1405-0002", second.MissionNumber);
        Assert.Equal(MissionStatus.Pending, second.Status);
        Assert.Equal(Hour(0, 12), second.PlannedStartAtUtc);
        Assert.Equal(Hour(0, 14), second.PlannedEndAtUtc);
        Assert.Equal(1_100L, second.OdometerStartKm);
        Assert.Null(second.OdometerEndKm);
    }

    [Fact]
    public async Task SplitAsync_LinksBothLegsToEachOther()
    {
        var running = await ConfirmRunningAsync();

        var first = await ServiceFor(MissionAdmin()).SplitAsync(
            running.Id, new MissionSplitRequest(Hour(0, 12), 1_100, null, null, "تغییر راننده", null));

        var second = Db.Missions.AsNoTracking().Single(item => item.Id != first.Id);
        Assert.Equal(second.Id, first.RelatedMissionId);
        Assert.Equal(second.MissionNumber, first.RelatedMissionNumber);
        Assert.Equal(running.Id, second.RelatedMissionId);
        Assert.NotNull(second.SplitAtUtc);
        Assert.Equal("تغییر راننده", second.SplitReason);
    }

    [Fact]
    public async Task SplitAsync_UsesSecondResourcesWhenProvided()
    {
        var running = await ConfirmRunningAsync();
        var first = Reload(running.Id);
        var otherDriver = NewDriver("حسین", "کاظمی");
        var otherVehicle = NewVehicle();
        Db.SaveChanges();

        await ServiceFor(MissionAdmin()).SplitAsync(
            running.Id,
            new MissionSplitRequest(Hour(0, 12), 1_100, otherDriver.Id, otherVehicle.Id, "تغییر تیم", null));

        var second = Db.Missions.AsNoTracking().Single(item => item.Id != running.Id);
        Assert.Equal(otherDriver.Id, second.DriverId);
        Assert.Equal(otherVehicle.Id, second.VehicleId);
        Assert.NotEqual(first.VehicleId, second.VehicleId);
    }

    [Fact]
    public async Task SplitAsync_OnConfirmedNotRunningMissionIsConflict()
    {
        var confirmed = await ConfirmAsync();

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin()).SplitAsync(
            confirmed.Id, new MissionSplitRequest(Hour(0, 12), 1_100, null, null, "تقسیم", null)));
    }

    [Fact]
    public async Task SplitAsync_WithSplitTimeAfterNowIsValidationFailure()
    {
        var running = await ConfirmRunningAsync();
        Clock.NowUtc = Hour(0, 11);

        await Assert.ThrowsAsync<ValidationAppException>(() => ServiceFor(MissionAdmin()).SplitAsync(
            running.Id, new MissionSplitRequest(Hour(0, 13), 1_100, null, null, "تقسیم", null)));
    }

    [Fact]
    public async Task SplitAsync_WhenSecondLegResourcesAreBusyIsConflict()
    {
        var running = await ConfirmRunningAsync();
        var resources = SaveReadyResources();
        await ConfirmAsync(resources, Hour(0, 13), Hour(0, 15));

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin()).SplitAsync(
            running.Id,
            new MissionSplitRequest(Hour(0, 12), 1_100, resources.Driver.Id, resources.Vehicle.Id, "تقسیم", null)));
        Assert.Contains("بخش دوم", error.Message);
        Assert.Equal(MissionStatus.Running, Reload(running.Id).Status);
    }

    [Fact]
    public async Task SplitAsync_WithoutSplitPermissionIsForbidden()
    {
        var running = await ConfirmRunningAsync();
        var service = ServiceFor(User(OtherUsername, PermissionCatalog.Missions.View));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.SplitAsync(
            running.Id, new MissionSplitRequest(Hour(0, 12), 1_100, null, null, "تقسیم", null)));
    }

    /// <summary>مأموریت تأییدشده‌ای که در ساعت ۱۰ شروع شده و اکنون در ساعت ۱۲ در حال اجراست.</summary>
    private async Task<MissionDetailsDto> ConfirmRunningAsync()
    {
        var confirmed = await ConfirmAsync(plannedStart: Hour(0, 10), plannedEnd: Hour(0, 14));
        await MarkRunningAsync(confirmed.Id, Hour(0, 10));
        Clock.NowUtc = Hour(0, 12);
        return confirmed;
    }

    /// <summary>
    /// بازبین درخواست تغییر؛ هویتی مستقل از ثبت‌کننده تا دامنه کنترل «تصمیم فقط با همان بازبین» سنجیده شود.
    /// </summary>
    private static FakeCurrentUser Reviewer() => User(ApproverUsername, PermissionCatalog.Missions.ChangeReview);

    private async Task<(Guid MissionId, Guid ChangeId)> RequestedChangeAsync()
    {
        var confirmed = await ConfirmAsync();
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تغییر ساعت",
                ProposedChange(confirmed, Hour(0, 11), Hour(0, 15))));
        return (confirmed.Id, details.ChangeRequests.Single().Id);
    }

    private async Task<(Guid MissionId, Guid ChangeId)> StartedChangeAsync()
    {
        var (missionId, changeId) = await RequestedChangeAsync();
        await ServiceFor(Reviewer()).ReviewChangeAsync(missionId, changeId, start: true);
        return (missionId, changeId);
    }
}

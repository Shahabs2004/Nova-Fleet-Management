using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.UnitTests.Missions;

/// <summary>
/// درخواست تغییر فقط می‌تواند روی همان وضعیت پایه اعمال شود و همه قواعد روز تصمیم دوباره سنجیده می‌شوند.
/// </summary>
public sealed class MissionChangeApprovalTests : MissionTestBase
{
    [Fact]
    public async Task RequestChangeAsync_PersistsIndependentBinaryBaseSnapshot()
    {
        var confirmed = await ConfirmAsync();
        byte[] expectedRowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Missions] SET [RowVersion] = {expectedRowVersion} WHERE [Id] = {confirmed.Id}");
        Db.ChangeTracker.Clear();
        var mission = Reload(confirmed.Id);

        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تغییر ساعت",
                ProposedChange(confirmed, Hour(0, 11), Hour(0, 15))));

        var changeId = Assert.Single(details.ChangeRequests).Id;
        var change = await Db.MissionChangeRequests.SingleAsync(item => item.Id == changeId);
        var snapshotProperty = Db.Entry(change).Property("BaseRowVersion");
        var snapshot = Assert.IsType<byte[]>(snapshotProperty.CurrentValue);
        Assert.Equal(expectedRowVersion, snapshot);
        Assert.NotSame(mission.RowVersion, snapshot);
        Assert.Equal(mission.Status, Assert.IsType<MissionStatus>(Db.Entry(change).Property("BaseStatus").CurrentValue));
        Assert.Equal(mission.PlannedStartAtUtc, Assert.IsType<DateTimeOffset>(Db.Entry(change).Property("BasePlannedStartAtUtc").CurrentValue));
        Assert.Equal(mission.PlannedEndAtUtc, Assert.IsType<DateTimeOffset>(Db.Entry(change).Property("BasePlannedEndAtUtc").CurrentValue));

        var entityType = Db.Model.FindEntityType(typeof(MissionChangeRequest));
        Assert.NotNull(entityType);
        var rowVersion = entityType.FindProperty("BaseRowVersion");
        Assert.NotNull(rowVersion);
        Assert.Equal("varbinary(8)", rowVersion.GetColumnType());
        Assert.Equal(ValueGenerated.Never, rowVersion.ValueGenerated);
        Assert.False(rowVersion.IsConcurrencyToken);

        using var verification = new NovaFleetDbContext(Options);
        var stored = await verification.MissionChangeRequests.AsNoTracking().SingleAsync(item => item.Id == changeId);
        Assert.Equal(expectedRowVersion, Assert.IsType<byte[]>(verification.Entry(stored).Property("BaseRowVersion").CurrentValue));
        Assert.Equal(mission.Status, Assert.IsType<MissionStatus>(verification.Entry(stored).Property("BaseStatus").CurrentValue));
        Assert.Equal(mission.PlannedStartAtUtc, Assert.IsType<DateTimeOffset>(verification.Entry(stored).Property("BasePlannedStartAtUtc").CurrentValue));
        Assert.Equal(mission.PlannedEndAtUtc, Assert.IsType<DateTimeOffset>(verification.Entry(stored).Property("BasePlannedEndAtUtc").CurrentValue));
    }

    [Theory]
    [InlineData("RowVersion")]
    [InlineData("Status")]
    [InlineData("PlannedStartAtUtc")]
    [InlineData("PlannedEndAtUtc")]
    public async Task DecideChangeAsync_WhenBaseMissionChangesAfterRequest_IsConflictWithoutMutation(string changedField)
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var mission = await Db.Missions.SingleAsync(item => item.Id == missionId);
        switch (changedField)
        {
            case "RowVersion":
                byte[] changedRowVersion = [9, 10, 11, 12, 13, 14, 15, 16];
                await Db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [Missions] SET [RowVersion] = {changedRowVersion} WHERE [Id] = {missionId}");
                break;
            case "Status":
                mission.StartAutomatically(Hour(0, 10));
                await Db.SaveChangesAsync();
                Clock.NowUtc = Hour(0, 12);
                break;
            case "PlannedStartAtUtc":
                Db.Entry(mission).Property(nameof(Mission.PlannedStartAtUtc)).CurrentValue = Hour(0, 9);
                await Db.SaveChangesAsync();
                break;
            case "PlannedEndAtUtc":
                Db.Entry(mission).Property(nameof(Mission.PlannedEndAtUtc)).CurrentValue = Hour(0, 16);
                await Db.SaveChangesAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(changedField));
        }

        Db.ChangeTracker.Clear();
        var before = Reload(missionId);
        var changeBefore = ReloadChange(changeId);
        var service = ServiceFor(Reviewer());

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => service.DecideChangeAsync(
            missionId, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains("دوباره ثبت", error.Message);
        AssertUnchangedAfterRejectedApproval(missionId, changeId, before, changeBefore);
    }

    [Fact]
    public async Task DecideChangeAsync_LegacyRequestWithoutBaseSnapshotIsStaleAndRequiresResubmission()
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var before = Reload(missionId);
        var change = await Db.MissionChangeRequests.SingleAsync(item => item.Id == changeId);
        Db.Entry(change).Property(nameof(MissionChangeRequest.BaseRowVersion)).CurrentValue = null;
        Db.Entry(change).Property(nameof(MissionChangeRequest.BaseStatus)).CurrentValue = null;
        Db.Entry(change).Property(nameof(MissionChangeRequest.BasePlannedStartAtUtc)).CurrentValue = null;
        Db.Entry(change).Property(nameof(MissionChangeRequest.BasePlannedEndAtUtc)).CurrentValue = null;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            missionId, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains("دوباره ثبت", error.Message);
        AssertUnchangedAfterRejectedApproval(missionId, changeId, before, changeBefore);
    }

    [Fact]
    public async Task DecideChangeAsync_WhenProposedStartLeavesCurrentBackdateWindow_IsRejectedWithoutMutation()
    {
        Settings.Values[SettingKeys.BackdateRequestDays] = "1";
        var confirmed = await ConfirmAsync();
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "ثبت ساعت گذشته",
                ProposedChange(confirmed, Hour(0, 6), Hour(0, 12))));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(Reviewer()).ReviewChangeAsync(confirmed.Id, changeId, start: true);
        Clock.NowUtc = Hour(2, 8);
        var before = Reload(confirmed.Id);
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ValidationAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains("1 روز گذشته", error.Message);
        AssertUnchangedAfterRejectedApproval(confirmed.Id, changeId, before, changeBefore);
    }

    [Fact]
    public async Task DecideChangeAsync_WhenProposedTimeExceedsCurrentFutureWindow_IsRejectedWithoutMutation()
    {
        var confirmed = await ConfirmAsync();
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "انتقال به آینده",
                ProposedChange(confirmed, Hour(5, 10), Hour(5, 14))));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(Reviewer()).ReviewChangeAsync(confirmed.Id, changeId, start: true);
        Settings.Values[SettingKeys.FutureRequestDays] = "1";
        var before = Reload(confirmed.Id);
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ValidationAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains("1 روز آینده", error.Message);
        AssertUnchangedAfterRejectedApproval(confirmed.Id, changeId, before, changeBefore);
    }

    [Fact]
    public async Task DecideChangeAsync_WhenApprovedStartIsPast_RecomputesBackdatedFlag()
    {
        var confirmed = await ConfirmAsync();
        Assert.False(confirmed.IsBackdated);
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تغییر به ساعت گذشته",
                ProposedChange(confirmed, Hour(0, 6), Hour(0, 12))));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(Reviewer()).ReviewChangeAsync(confirmed.Id, changeId, start: true);

        var result = await ServiceFor(Reviewer()).DecideChangeAsync(
            confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت"));

        Assert.True(result.IsBackdated);
        Assert.True(Reload(confirmed.Id).IsBackdated);
    }

    [Fact]
    public async Task DecideChangeAsync_WhenApprovedStartIsFuture_ClearsExistingBackdatedFlag()
    {
        var service = ServiceFor(MissionAdmin());
        var created = await service.CreateAsync(Request(Hour(-1, 6), Hour(-1, 9)));
        await service.StartReviewAsync(created.Id);
        var resources = SaveReadyResources();
        var confirmed = await service.ApproveAsync(created.Id,
            new MissionApprovalRequest(resources.Driver.Id, resources.Vehicle.Id, "تأیید آزمون"));
        Assert.True(confirmed.IsBackdated);
        var details = await service.RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تغییر به ساعت آینده",
                ProposedChange(confirmed, Hour(1, 10), Hour(1, 14))));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(Reviewer()).ReviewChangeAsync(confirmed.Id, changeId, start: true);

        var result = await ServiceFor(Reviewer()).DecideChangeAsync(
            confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت"));

        Assert.False(result.IsBackdated);
        Assert.False(Reload(confirmed.Id).IsBackdated);
    }

    [Theory]
    [InlineData("Driver")]
    [InlineData("Vehicle")]
    public async Task DecideChangeAsync_WhenProposedResourceBecomesIneligible_IsConflictWithoutMutation(string resource)
    {
        var confirmed = await ConfirmAsync();
        var replacement = SaveReadyResources();
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تعویض منابع",
                ProposedChange(
                    confirmed,
                    Hour(0, 11),
                    Hour(0, 15),
                    replacement.Driver.Id,
                    replacement.Vehicle.Id,
                    5_500)));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(Reviewer()).ReviewChangeAsync(confirmed.Id, changeId, start: true);
        if (resource == "Driver")
        {
            replacement.Driver.Archive();
        }
        else
        {
            replacement.Vehicle.ChangeManualStatus(ManualVehicleStatus.RepairShop, hasActiveMission: false);
        }

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var before = Reload(confirmed.Id);
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains(resource == "Driver" ? "راننده" : "خودرو", error.Message);
        AssertUnchangedAfterRejectedApproval(confirmed.Id, changeId, before, changeBefore);
    }

    [Fact]
    public async Task DecideChangeAsync_WhenConflictAppearsAfterRequest_IsConflictWithoutMutation()
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var baseMission = await Db.Missions.SingleAsync(item => item.Id == missionId);
        var driver = await Db.Drivers.SingleAsync(item => item.Id == baseMission.DriverId);
        var vehicle = await Db.Vehicles.SingleAsync(item => item.Id == baseMission.VehicleId);
        var later = await ConfirmAsync((driver, vehicle), Hour(1, 10), Hour(1, 14));
        var otherMission = await Db.Missions.SingleAsync(item => item.Id == later.Id);
        Db.Entry(otherMission).Property(nameof(Mission.PlannedStartAtUtc)).CurrentValue = Hour(0, 12);
        Db.Entry(otherMission).Property(nameof(Mission.PlannedEndAtUtc)).CurrentValue = Hour(0, 16);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var before = Reload(missionId);
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            missionId, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains("تداخل", error.Message);
        AssertUnchangedAfterRejectedApproval(missionId, changeId, before, changeBefore);
    }

    [Theory]
    [InlineData("Driver")]
    [InlineData("Vehicle")]
    public async Task DecideChangeAsync_WhenStoredProposalMissesRequiredResource_IsValidationFailureWithoutMutation(string resource)
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var details = await ServiceFor(MissionAdmin()).GetAsync(missionId);
        var proposed = ProposedChange(details, Hour(0, 11), Hour(0, 15));
        proposed = proposed with
        {
            DriverId = resource == "Driver" ? null : proposed.DriverId,
            VehicleId = resource == "Vehicle" ? null : proposed.VehicleId,
        };
        var change = await Db.MissionChangeRequests.SingleAsync(item => item.Id == changeId);
        Db.Entry(change).Property(nameof(MissionChangeRequest.ProposedValuesJson)).CurrentValue =
            JsonSerializer.Serialize(proposed, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var before = Reload(missionId);
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ValidationAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            missionId, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains(resource == "Driver" ? "راننده" : "خودرو", error.Message);
        AssertUnchangedAfterRejectedApproval(missionId, changeId, before, changeBefore);
    }

    [Theory]
    [InlineData(nameof(MissionChangeRequest.BaseRowVersion))]
    [InlineData(nameof(MissionChangeRequest.BaseStatus))]
    [InlineData(nameof(MissionChangeRequest.BasePlannedStartAtUtc))]
    [InlineData(nameof(MissionChangeRequest.BasePlannedEndAtUtc))]
    public async Task DecideChangeAsync_WhenStoredBaseSnapshotComponentDiffers_IsConflictWithoutMutation(string component)
    {
        var (missionId, changeId) = await StartedChangeAsync();
        var change = await Db.MissionChangeRequests.SingleAsync(item => item.Id == changeId);
        switch (component)
        {
            case nameof(MissionChangeRequest.BaseRowVersion):
                Db.Entry(change).Property(component).CurrentValue = new byte[] { 21, 22, 23, 24, 25, 26, 27, 28 };
                break;
            case nameof(MissionChangeRequest.BaseStatus):
                Db.Entry(change).Property(component).CurrentValue = MissionStatus.Completed;
                break;
            case nameof(MissionChangeRequest.BasePlannedStartAtUtc):
                Db.Entry(change).Property(component).CurrentValue = Hour(0, 9);
                break;
            case nameof(MissionChangeRequest.BasePlannedEndAtUtc):
                Db.Entry(change).Property(component).CurrentValue = Hour(0, 16);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(component));
        }

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var before = Reload(missionId);
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            missionId, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains("دوباره ثبت", error.Message);
        AssertUnchangedAfterRejectedApproval(missionId, changeId, before, changeBefore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5_500L)]
    public async Task DecideChangeAsync_WhenRunningMissionVehicleChanges_IsConflictWithoutMutationOrSideEffects(long? replacementOdometer)
    {
        var confirmed = await ConfirmAsync();
        var replacement = SaveReadyResources();
        await MarkRunningAsync(confirmed.Id, Hour(0, 10));
        Clock.NowUtc = Hour(0, 12);
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تعویض خودرو در حال اجرا",
                ProposedChange(
                    confirmed,
                    Hour(0, 11),
                    Hour(0, 15),
                    replacement.Driver.Id,
                    replacement.Vehicle.Id,
                    replacementOdometer)));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(Reviewer()).ReviewChangeAsync(confirmed.Id, changeId, start: true);
        var before = Reload(confirmed.Id);
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains("در حال اجرا", error.Message);
        AssertUnchangedAfterRejectedApproval(confirmed.Id, changeId, before, changeBefore);
        Assert.DoesNotContain(Sms.Queued, item => item.EventType is SmsEventType.DriverOrVehicleChanged or SmsEventType.MissionChanged);
        Assert.NotEqual("mission.change.approved", Notifications.LastEventKeyFor(confirmed.RequesterUsername));
    }

    [Fact]
    public async Task DecideChangeAsync_WhenMinimumGapIncreasesAfterRequest_RechecksConflictWithoutMutation()
    {
        Settings.Values[SettingKeys.MinimumGapMinutes] = "15";
        var confirmed = await ConfirmAsync();
        var baseMission = await Db.Missions.SingleAsync(item => item.Id == confirmed.Id);
        var driver = await Db.Drivers.SingleAsync(item => item.Id == baseMission.DriverId);
        var vehicle = await Db.Vehicles.SingleAsync(item => item.Id == baseMission.VehicleId);
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تغییر ساعت",
                ProposedChange(confirmed, Hour(0, 11), Hour(0, 15))));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(Reviewer()).ReviewChangeAsync(confirmed.Id, changeId, start: true);

        var later = await ConfirmAsync((driver, vehicle), Hour(1, 10), Hour(1, 14));
        var otherMission = await Db.Missions.SingleAsync(item => item.Id == later.Id);
        Db.Entry(otherMission).Property(nameof(Mission.PlannedStartAtUtc)).CurrentValue = Hour(0, 10).AddMinutes(20);
        Db.Entry(otherMission).Property(nameof(Mission.PlannedEndAtUtc)).CurrentValue = Hour(0, 10).AddMinutes(40);
        Settings.Values[SettingKeys.MinimumGapMinutes] = "30";
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var before = Reload(confirmed.Id);
        var changeBefore = ReloadChange(changeId);

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(Reviewer()).DecideChangeAsync(
            confirmed.Id, changeId, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Contains("تداخل", error.Message);
        AssertUnchangedAfterRejectedApproval(confirmed.Id, changeId, before, changeBefore);
    }

    private async Task<(Guid MissionId, Guid ChangeId)> StartedChangeAsync()
    {
        var confirmed = await ConfirmAsync();
        var details = await ServiceFor(MissionAdmin()).RequestChangeAsync(
            confirmed.Id,
            new MissionChangeRequestInput(
                "تغییر ساعت",
                ProposedChange(confirmed, Hour(0, 11), Hour(0, 15))));
        var changeId = details.ChangeRequests.Single().Id;
        await ServiceFor(Reviewer()).ReviewChangeAsync(confirmed.Id, changeId, start: true);
        return (confirmed.Id, changeId);
    }

    private static FakeCurrentUser Reviewer() => User(ApproverUsername, PermissionCatalog.Missions.ChangeReview);

    private void AssertUnchangedAfterRejectedApproval(
        Guid missionId,
        Guid changeId,
        Mission before,
        MissionChangeRequest changeBefore)
    {
        Db.ChangeTracker.Clear();
        var after = Reload(missionId);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.PlannedStartAtUtc, after.PlannedStartAtUtc);
        Assert.Equal(before.PlannedEndAtUtc, after.PlannedEndAtUtc);
        Assert.Equal(before.DriverId, after.DriverId);
        Assert.Equal(before.VehicleId, after.VehicleId);
        Assert.Equal(before.OdometerStartKm, after.OdometerStartKm);
        Assert.Equal(before.OdometerEndKm, after.OdometerEndKm);
        Assert.Equal(before.IsBackdated, after.IsBackdated);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.True(before.RowVersion.AsSpan().SequenceEqual(after.RowVersion));

        var changeAfter = ReloadChange(changeId);
        Assert.Equal(changeBefore.Status, changeAfter.Status);
        Assert.Equal(changeBefore.ReviewerUsername, changeAfter.ReviewerUsername);
        Assert.Equal(changeBefore.ReviewStartedAtUtc, changeAfter.ReviewStartedAtUtc);
        Assert.Null(changeAfter.ReviewedAtUtc);
        Assert.Null(changeAfter.DecisionReason);
        Assert.False(Audit.Wrote("ApproveMissionChange"));
    }
}

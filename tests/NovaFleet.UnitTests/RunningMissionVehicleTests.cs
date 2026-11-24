using NovaFleet.Domain.Missions;

namespace NovaFleet.UnitTests;

/// <summary>
/// تغییر خودرو پس از شروع مأموریت باید از مسیر تقسیم انجام شود، نه بازنویسی کیلومترهای مأموریت در حال اجرا.
/// </summary>
public sealed class RunningMissionVehicleTests
{
    private static readonly TimeSpan TehranOffset = TimeSpan.FromMinutes(210);
    private static readonly DateTimeOffset PlannedStart = new(2026, 11, 15, 10, 0, 0, TehranOffset);
    private static readonly DateTimeOffset PlannedEnd = new(2026, 11, 15, 14, 0, 0, TehranOffset);
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TehranOffset);
    private static readonly Guid DriverId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VehicleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(null)]
    [InlineData(5_500L)]
    public void ApplyApprovedChanges_RunningMissionRejectsVehicleChangeAndPreservesOdometers(long? replacementStartKm)
    {
        var mission = RunningMission();
        var changes = ChangesFor(mission) with
        {
            VehicleId = Guid.NewGuid(),
            OdometerStartKm = replacementStartKm,
        };

        var error = Assert.Throws<InvalidOperationException>(() => mission.ApplyApprovedChanges(changes, Now));

        Assert.Contains("در حال اجرا", error.Message);
        Assert.Equal(VehicleId, mission.VehicleId);
        Assert.Equal(1_000L, mission.OdometerStartKm);
        Assert.Equal(1_100L, mission.OdometerEndKm);
        Assert.Equal(100L, mission.DistanceKm);
    }

    [Fact]
    public void ApplyApprovedChanges_NotStartedMissionStillAllowsVehicleAndStartOdometerChange()
    {
        var mission = ConfirmedMission();
        mission.SetOdometerStart(1_000);
        mission.SetOdometerEnd(1_100);
        var replacementVehicleId = Guid.NewGuid();

        mission.ApplyApprovedChanges(ChangesFor(mission) with
        {
            VehicleId = replacementVehicleId,
            OdometerStartKm = 5_500,
        }, Now);

        Assert.Equal(replacementVehicleId, mission.VehicleId);
        Assert.Equal(5_500L, mission.OdometerStartKm);
        Assert.Null(mission.OdometerEndKm);
    }

    [Fact]
    public void ApplyApprovedChanges_CompletedMissionStillRejectsChangesAndPreservesOdometers()
    {
        var mission = ConfirmedMission();
        mission.StartAutomatically(PlannedStart);
        mission.SetOdometerStart(1_000);
        mission.SetOdometerEnd(1_100);
        mission.CompleteAutomatically(PlannedEnd);
        var changes = ChangesFor(mission) with
        {
            VehicleId = Guid.NewGuid(),
            OdometerStartKm = 5_500,
        };

        Assert.Throws<InvalidOperationException>(() => mission.ApplyApprovedChanges(changes, PlannedEnd));

        Assert.Equal(VehicleId, mission.VehicleId);
        Assert.Equal(1_000L, mission.OdometerStartKm);
        Assert.Equal(1_100L, mission.OdometerEndKm);
        Assert.Equal(MissionStatus.Completed, mission.Status);
    }

    private static Mission RunningMission()
    {
        var mission = ConfirmedMission();
        mission.StartAutomatically(PlannedStart);
        mission.SetOdometerStart(1_000);
        mission.SetOdometerEnd(1_100);
        return mission;
    }

    private static Mission ConfirmedMission()
    {
        var mission = new Mission(
            "1405-0001",
            "انتقال بار",
            "demo.requester",
            "مریم رضایی",
            "09123456789",
            "واحد ترانسفر",
            MissionType.Freight,
            "انتقال کالا",
            "شیراز",
            "بندرعباس",
            ["جهرم"],
            PlannedStart,
            PlannedEnd);
        mission.Approve(DriverId, VehicleId, "demo.approver", PlannedStart.AddHours(-2));
        return mission;
    }

    private static MissionChanges ChangesFor(Mission mission) => new(
        mission.Title,
        mission.Type,
        mission.Purpose,
        mission.Origin,
        mission.Destination,
        mission.GetIntermediateStops(),
        mission.PlannedStartAtUtc,
        mission.PlannedEndAtUtc,
        mission.DriverId!.Value,
        mission.VehicleId!.Value,
        mission.RequesterName,
        mission.RequesterPhoneSnapshot,
        mission.RequesterOrganizationalUnit,
        mission.Description,
        mission.PassengerCount,
        mission.MissionOrderNumber,
        mission.MissionOrderDate,
        mission.CargoDescription,
        mission.ConsigneeName,
        mission.PassengerNames,
        mission.OdometerStartKm);
}

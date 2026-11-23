using NovaFleet.Application.Authorization;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Dashboard;

namespace NovaFleet.UnitTests.Missions;

public sealed class DashboardVisibilityTests : MissionTestBase
{
    [Fact]
    public async Task OwnOnlyUser_SeesOnlyOwnMissionCountsChartUpcomingAndWorkload()
    {
        await SeedMissions();
        var service = new DashboardService(Db,
            User(RequesterUsername,
                PermissionCatalog.Dashboard.View,
                PermissionCatalog.Missions.ViewOwn,
                PermissionCatalog.Drivers.View),
            Clock);

        var stats = await service.GetAsync();

        Assert.Equal(1, stats.PendingRequests);
        Assert.Equal(1, stats.ActiveMissions);
        Assert.Equal(1, stats.PendingChangeRequests);
        Assert.Equal(3, stats.MissionsThisMonth);
        Assert.Equal(25, stats.DistanceThisMonthKm);
        Assert.Equal(3, stats.MissionsByDay.Sum(point => point.Value));
        Assert.Equal("OWN-ONLY active", Assert.Single(stats.UpcomingMissions).Title);
        Assert.Equal("راننده خود", Assert.Single(stats.DriverWorkload).Name);
        Assert.Equal(1, stats.DriverWorkload[0].Value);
    }

    [Fact]
    public async Task UserWithMissionView_SeesAllMissionCountsChartUpcomingAndWorkload()
    {
        await SeedMissions();
        var service = new DashboardService(Db,
            User(RequesterUsername,
                PermissionCatalog.Dashboard.View,
                PermissionCatalog.Missions.View,
                PermissionCatalog.Drivers.View),
            Clock);

        var stats = await service.GetAsync();

        Assert.Equal(2, stats.PendingRequests);
        Assert.Equal(2, stats.ActiveMissions);
        Assert.Equal(2, stats.PendingChangeRequests);
        Assert.Equal(6, stats.MissionsThisMonth);
        Assert.Equal(90, stats.DistanceThisMonthKm);
        Assert.Equal(6, stats.MissionsByDay.Sum(point => point.Value));
        Assert.Equivalent(new[] { "OWN-ONLY active", "OTHER-ONLY active" }, stats.UpcomingMissions.Select(item => item.Title));
        Assert.Equivalent(new[] { "راننده خود", "راننده دیگر" }, stats.DriverWorkload.Select(item => item.Name));
        Assert.All(stats.DriverWorkload, item => Assert.Equal(1, item.Value));
    }

    [Fact]
    public async Task UserWithoutMissionPermissions_KeepsGlobalVehicleReadiness()
    {
        await SeedMissions();
        var service = new DashboardService(Db,
            User(RequesterUsername,
                PermissionCatalog.Dashboard.View,
                PermissionCatalog.Vehicles.View),
            Clock);

        var stats = await service.GetAsync();

        Assert.Equal(0, stats.ReadyVehicles);
        Assert.Contains(stats.VehicleStatuses, point => point.Label == "در مأموریت" && point.Value == 2);
        Assert.Empty(stats.UpcomingMissions);
        Assert.Empty(stats.MissionsByDay);
    }

    private async Task SeedMissions()
    {
        Clock.NowUtc = Hour(0, 12);
        var ownDriver = NewDriver("راننده", "خود");
        var otherDriver = NewDriver("راننده", "دیگر");
        var ownVehicle = NewVehicle();
        var otherVehicle = NewVehicle();

        var ownPending = NewMission(RequesterUsername, "OWN-ONLY pending", Hour(0, 10), Hour(0, 14));
        var otherPending = NewMission(OtherUsername, "OTHER-ONLY pending", Hour(0, 11), Hour(0, 15));
        var ownActive = NewMission(RequesterUsername, "OWN-ONLY active", Hour(0, 14), Hour(0, 18));
        var otherActive = NewMission(OtherUsername, "OTHER-ONLY active", Hour(0, 15), Hour(0, 19));
        ownActive.Approve(ownDriver.Id, ownVehicle.Id, ApproverUsername, Clock.UtcNow);
        otherActive.Approve(otherDriver.Id, otherVehicle.Id, ApproverUsername, Clock.UtcNow);
        ownActive.SetOdometerStart(1_000);
        ownActive.SetOdometerEnd(1_025);
        otherActive.SetOdometerStart(2_000);
        otherActive.SetOdometerEnd(2_065);

        var ownCompleted = NewMission(RequesterUsername, "OWN-ONLY completed", Hour(0, 16), Hour(0, 20));
        var otherCompleted = NewMission(OtherUsername, "OTHER-ONLY completed", Hour(0, 17), Hour(0, 21));
        ownCompleted.Approve(ownDriver.Id, ownVehicle.Id, ApproverUsername, Clock.UtcNow);
        otherCompleted.Approve(otherDriver.Id, otherVehicle.Id, ApproverUsername, Clock.UtcNow);
        ownCompleted.SetOdometerStart(1_000);
        ownCompleted.SetOdometerEnd(1_025);
        otherCompleted.SetOdometerStart(2_000);
        otherCompleted.SetOdometerEnd(2_065);
        ownCompleted.StartAutomatically(Hour(0, 16));
        otherCompleted.StartAutomatically(Hour(0, 17));
        ownCompleted.CompleteAutomatically(Hour(0, 20));
        otherCompleted.CompleteAutomatically(Hour(0, 21));
        Clock.NowUtc = Hour(0, 12);

        Db.Missions.AddRange(ownPending, otherPending, ownActive, otherActive, ownCompleted, otherCompleted);
        Db.MissionChangeRequests.AddRange(
            new MissionChangeRequest(ownActive.Id, RequesterUsername, "تغییر خود", "{}"),
            new MissionChangeRequest(otherActive.Id, OtherUsername, "تغییر دیگر", "{}"));
        await Db.SaveChangesAsync();
    }

    private Mission NewMission(string username, string title, DateTimeOffset start, DateTimeOffset end) =>
        new($"1405-{Guid.NewGuid():N}"[..20], title, username, title, "09123456789", "واحد آزمون",
            MissionType.Freight, null, "شیراز", "تهران", null, start, end);
}

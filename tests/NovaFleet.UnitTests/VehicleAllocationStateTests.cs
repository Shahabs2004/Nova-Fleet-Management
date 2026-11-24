using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Vehicles;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Vehicles;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests;

public sealed class VehicleAllocationStateTests : MissionTestBase
{
    [Fact]
    public async Task Archive_RejectsFutureConfirmedAllocation()
    {
        var (driver, vehicle) = SaveReadyResources();
        await ConfirmAsync((driver, vehicle), Hour(1, 10), Hour(1, 14));

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => VehicleService().SetArchivedAsync(vehicle.Id, true));

        Assert.Contains("مأموریت فعال", error.Message);
        Assert.False((await Db.Vehicles.AsNoTracking().SingleAsync(item => item.Id == vehicle.Id)).IsArchived);
    }

    [Fact]
    public async Task ManualStatus_RejectsFutureConfirmedAllocation()
    {
        var (driver, vehicle) = SaveReadyResources();
        await ConfirmAsync((driver, vehicle), Hour(1, 10), Hour(1, 14));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            VehicleService().SetManualStatusAsync(vehicle.Id, ManualVehicleStatus.RepairShop, null));

        Assert.Contains("مأموریت فعال", error.Message);
        Assert.Equal(ManualVehicleStatus.Ready,
            (await Db.Vehicles.AsNoTracking().SingleAsync(item => item.Id == vehicle.Id)).ManualStatus);
    }

    [Fact]
    public async Task Archive_AllowsVehicleAfterMissionCompletes()
    {
        var (driver, vehicle) = SaveReadyResources();
        var confirmed = await ConfirmAsync((driver, vehicle));
        await MarkRunningAsync(confirmed.Id, Hour(0, 10), odometerStartKm: null);
        Clock.NowUtc = Hour(0, 14);
        var mission = await Db.Missions.SingleAsync(item => item.Id == confirmed.Id);
        mission.CompleteAutomatically(Clock.UtcNow);
        await Db.SaveChangesAsync();

        var result = await VehicleService().SetArchivedAsync(vehicle.Id, true);

        Assert.True(result.IsArchived);
    }

    [Fact]
    public async Task Archive_AllowsVehicleNotAssignedToPendingMission()
    {
        var vehicle = NewVehicle();
        await Db.SaveChangesAsync();
        await CreatePendingAsync();

        var result = await VehicleService().SetArchivedAsync(vehicle.Id, true);

        Assert.True(result.IsArchived);
    }

    private VehicleService VehicleService() => new(
        Db,
        User(RequesterUsername, PermissionCatalog.Vehicles.Manage, PermissionCatalog.Vehicles.View),
        Audit);
}

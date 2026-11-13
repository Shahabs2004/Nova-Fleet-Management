using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Infrastructure.Drivers;
using NovaFleet.UnitTests.Missions;
using Xunit;

namespace NovaFleet.UnitTests.Drivers;

/// <summary>رابطه راننده و خودروی پیش‌فرض یک‌به‌یک است؛ هر خودرو حداکثر یک راننده پیش‌فرض دارد.</summary>
public sealed class DriverDefaultVehicleTests : MissionTestBase
{
    private DriverService ServiceForDriver(ICurrentUser user) => new(Db, user, Audit);

    private static ICurrentUser Admin() => User("demo.driveradmin", PermissionCatalog.Drivers.Manage, PermissionCatalog.Drivers.View);

    [Fact]
    public async Task Assigning_default_vehicle_to_second_driver_releases_first_holder()
    {
        var (first, vehicle) = SaveReadyResources();
        var second = NewDriver("علی", "اکبری");
        Db.SaveChanges();

        var service = ServiceForDriver(Admin());
        await service.SetDefaultVehicleAsync(first.Id, vehicle.Id, null);
        await service.SetDefaultVehicleAsync(second.Id, vehicle.Id, null);

        Assert.Null(Db.Drivers.AsNoTracking().First(d => d.Id == first.Id).DefaultVehicleId);
        Assert.Equal(vehicle.Id, Db.Drivers.AsNoTracking().First(d => d.Id == second.Id).DefaultVehicleId);
    }

    [Fact]
    public async Task Setting_vehicle_driver_replaces_previous_holder()
    {
        var (first, vehicle) = SaveReadyResources();
        var second = NewDriver("علی", "اکبری");
        first.SetDefaultVehicle(vehicle.Id);
        Db.SaveChanges();

        await ServiceForDriver(Admin()).SetVehicleDriverAsync(vehicle.Id, second.Id, null);

        Assert.Null(Db.Drivers.AsNoTracking().First(d => d.Id == first.Id).DefaultVehicleId);
        Assert.Equal(vehicle.Id, Db.Drivers.AsNoTracking().First(d => d.Id == second.Id).DefaultVehicleId);
    }

    [Fact]
    public async Task Clearing_vehicle_driver_releases_every_holder()
    {
        var (first, vehicle) = SaveReadyResources();
        var second = NewDriver("علی", "اکبری");
        first.SetDefaultVehicle(vehicle.Id);
        second.SetDefaultVehicle(vehicle.Id);
        Db.SaveChanges();

        await ServiceForDriver(Admin()).SetVehicleDriverAsync(vehicle.Id, null, null);

        Assert.Null(Db.Drivers.AsNoTracking().First(d => d.Id == first.Id).DefaultVehicleId);
        Assert.Null(Db.Drivers.AsNoTracking().First(d => d.Id == second.Id).DefaultVehicleId);
    }

    [Fact]
    public async Task Archived_vehicle_cannot_be_default()
    {
        var (driver, vehicle) = SaveReadyResources();
        vehicle.Archive();
        Db.SaveChanges();

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            ServiceForDriver(Admin()).SetDefaultVehicleAsync(driver.Id, vehicle.Id, null));
    }
}
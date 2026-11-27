using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Drivers;
using NovaFleet.Application.Vehicles;
using NovaFleet.Domain.Drivers;
using NovaFleet.Infrastructure.Drivers;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Security;
using NovaFleet.Infrastructure.Vehicles;
using NovaFleet.UnitTests.Missions;
using Xunit;

namespace NovaFleet.UnitTests.Drivers;

public sealed class DriverVehicleAtomicSaveTests : MissionTestBase
{
    private static ICurrentUser Admin() => User(
        "demo.driveradmin",
        PermissionCatalog.Drivers.Manage,
        PermissionCatalog.Drivers.DefaultVehicleManage,
        PermissionCatalog.Drivers.View,
        PermissionCatalog.Vehicles.Manage,
        PermissionCatalog.Vehicles.View);

    private DriverService Drivers => new(Db, Admin(), Audit);
    private VehicleService Vehicles => new(Db, Admin(), Audit);

    private static DriverCompositeCreateRequest NewDriver(Guid? vehicleId = null, string mobile = "09120000001") =>
        new(new DriverCreateRequest("رضا", "محمدی", mobile), vehicleId);

    private static DriverCompositeUpdateRequest EditDriver(string firstName, Guid? vehicleId, string? rowVersion = null) =>
        new(new DriverUpdateRequest(firstName, "محمدی", "09121112233", rowVersion), vehicleId);

    private static VehicleCreateRequest VehicleRequest() =>
        new("پژو", "۲۰۶", "12", "ب", "345", "67");

    private static VehicleCompositeUpdateRequest EditVehicle(string brand, Guid? driverId, string? rowVersion = null) =>
        new(new VehicleUpdateRequest(brand, "دیلی", "12", "ب", "345", "67", rowVersion), driverId);

    [Fact]
    public async Task Driver_create_with_missing_vehicle_rolls_back_new_driver()
    {
        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            Drivers.CreateCompositeAsync(NewDriver(Guid.NewGuid())));

        Assert.False(Db.Drivers.AsNoTracking().Any(item => item.MobilePhone == "09120000001"));
    }

    [Fact]
    public async Task Driver_update_rolls_back_profile_when_default_vehicle_does_not_exist()
    {
        var (driver, _) = SaveReadyResources();
        Db.ChangeTracker.Clear();
        var original = Db.Drivers.AsNoTracking().Single(item => item.Id == driver.Id);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            Drivers.UpdateCompositeAsync(driver.Id, EditDriver("نام جدید", Guid.NewGuid(), original.RowVersion.ToRowVersionString())));

        Db.ChangeTracker.Clear();
        var persisted = Db.Drivers.AsNoTracking().Single(item => item.Id == driver.Id);
        Assert.Equal(original.FirstName, persisted.FirstName);
        Assert.Equal(original.RowVersion, persisted.RowVersion);
        Assert.Null(persisted.DefaultVehicleId);
    }

    [Fact]
    public async Task Driver_composite_update_rolls_back_profile_when_old_holder_update_fails()
    {
        var (_, vehicle) = SaveReadyResources();
        var oldHolder = NewDriver("راننده", "قبلی");
        oldHolder.SetDefaultVehicle(vehicle.Id);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        var original = Db.Drivers.AsNoTracking().Single(item => item.Id == oldHolder.Id);
        var target = NewDriver("راننده", "جدید");
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        Db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER fail_old_holder_update BEFORE UPDATE ON Drivers
            WHEN OLD.FirstName = 'راننده' AND OLD.LastName = 'قبلی' AND NEW.DefaultVehicleId IS NULL
            BEGIN SELECT RAISE(ABORT, 'related holder write failed'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Drivers.UpdateCompositeAsync(target.Id, EditDriver("نام تغییرکرده", vehicle.Id,
                Db.Drivers.AsNoTracking().Single(item => item.Id == target.Id).RowVersion.ToRowVersionString())));

        Db.ChangeTracker.Clear();
        var persistedTarget = Db.Drivers.AsNoTracking().Single(item => item.Id == target.Id);
        var persistedHolder = Db.Drivers.AsNoTracking().Single(item => item.Id == oldHolder.Id);
        Assert.Equal("راننده", persistedTarget.FirstName);
        Assert.Null(persistedTarget.DefaultVehicleId);
        Assert.Equal(vehicle.Id, persistedHolder.DefaultVehicleId);
        Assert.Equal(original.RowVersion, persistedHolder.RowVersion);
    }

    [Fact]
    public async Task Driver_composite_create_saves_profile_and_default_vehicle_together()
    {
        var (_, vehicle) = SaveReadyResources();

        var saved = await Drivers.CreateCompositeAsync(NewDriver(vehicle.Id));

        Assert.Equal(vehicle.Id, saved.DefaultVehicleId);
        Assert.Equal(vehicle.PlateDisplay, saved.DefaultVehiclePlate);
        Assert.Null(saved.AvatarDataUrl);
    }

    [Fact]
    public async Task Vehicle_create_with_missing_driver_rolls_back_new_vehicle()
    {
        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            Vehicles.CreateCompositeAsync(new VehicleCompositeCreateRequest(VehicleRequest(), Guid.NewGuid())));

        Assert.False(Db.Vehicles.AsNoTracking().Any(item => item.Brand == "پژو"));
    }

    [Fact]
    public async Task Vehicle_update_rolls_back_profile_when_driver_does_not_exist()
    {
        var (_, vehicle) = SaveReadyResources();
        Db.ChangeTracker.Clear();
        var original = Db.Vehicles.AsNoTracking().Single(item => item.Id == vehicle.Id);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            Vehicles.UpdateCompositeAsync(vehicle.Id, EditVehicle("برند جدید", Guid.NewGuid(), original.RowVersion.ToRowVersionString())));

        Db.ChangeTracker.Clear();
        var persisted = Db.Vehicles.AsNoTracking().Single(item => item.Id == vehicle.Id);
        Assert.Equal(original.Brand, persisted.Brand);
        Assert.Equal(original.RowVersion, persisted.RowVersion);
    }

    [Fact]
    public async Task Vehicle_composite_update_rolls_back_profile_when_old_holder_update_fails()
    {
        var (_, vehicle) = SaveReadyResources();
        var oldHolder = NewDriver("راننده", "قبلی");
        oldHolder.SetDefaultVehicle(vehicle.Id);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        var originalHolder = Db.Drivers.AsNoTracking().Single(item => item.Id == oldHolder.Id);
        var target = NewDriver("راننده", "جدید");
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        Db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER fail_vehicle_old_holder_update BEFORE UPDATE ON Drivers
            WHEN OLD.FirstName = 'راننده' AND OLD.LastName = 'قبلی' AND NEW.DefaultVehicleId IS NULL
            BEGIN SELECT RAISE(ABORT, 'related holder write failed'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Vehicles.UpdateCompositeAsync(vehicle.Id, EditVehicle("برند تغییرکرده", target.Id,
                Db.Vehicles.AsNoTracking().Single(item => item.Id == vehicle.Id).RowVersion.ToRowVersionString())));

        Db.ChangeTracker.Clear();
        var persistedVehicle = Db.Vehicles.AsNoTracking().Single(item => item.Id == vehicle.Id);
        var persistedTarget = Db.Drivers.AsNoTracking().Single(item => item.Id == target.Id);
        var persistedHolder = Db.Drivers.AsNoTracking().Single(item => item.Id == oldHolder.Id);
        Assert.Equal("ایویکو", persistedVehicle.Brand);
        Assert.Null(persistedTarget.DefaultVehicleId);
        Assert.Equal(vehicle.Id, persistedHolder.DefaultVehicleId);
        Assert.Equal(originalHolder.RowVersion, persistedHolder.RowVersion);
    }

    [Fact]
    public async Task Vehicle_composite_create_saves_profile_and_driver_together()
    {
        var (driver, _) = SaveReadyResources();

        var saved = await Vehicles.CreateCompositeAsync(new VehicleCompositeCreateRequest(VehicleRequest(), driver.Id));

        Assert.Equal(saved.Id, Db.Drivers.AsNoTracking().Single(item => item.Id == driver.Id).DefaultVehicleId);
        Assert.Equal("پژو", saved.Brand);
    }

    [Fact]
    public async Task Vehicle_read_models_include_current_driver_even_when_driver_is_archived()
    {
        var (driver, vehicle) = SaveReadyResources();
        driver.SetDefaultVehicle(vehicle.Id);
        driver.Archive();
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var detail = await Vehicles.GetAsync(vehicle.Id);
        var page = await Vehicles.SearchAsync(null, false, 1, 20);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        foreach (var dto in new[] { detail, page.Items.Single() })
        {
            var json = JsonSerializer.SerializeToElement(dto, jsonOptions);
            Assert.True(json.TryGetProperty("driverId", out var driverId));
            Assert.True(json.TryGetProperty("driverDisplayName", out var driverName));
            Assert.Equal(driver.Id.ToString(), driverId.GetString());
            Assert.Equal(driver.DisplayName, driverName.GetString());
        }
    }

    [Fact]
    public async Task Vehicle_read_models_hide_driver_identity_without_driver_view_permission()
    {
        var (driver, vehicle) = SaveReadyResources();
        driver.SetDefaultVehicle(vehicle.Id);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var service = new VehicleService(Db, User("vehicle.view.only", PermissionCatalog.Vehicles.View), Audit);

        var detail = await service.GetAsync(vehicle.Id);
        var page = await service.SearchAsync(null, false, 1, 20);

        Assert.Null(detail.DriverId);
        Assert.Null(detail.DriverDisplayName);
        Assert.Null(page.Items.Single().DriverId);
        Assert.Null(page.Items.Single().DriverDisplayName);
    }

    [Fact]
    public async Task Composite_saves_require_profile_permission_and_driver_relationship_permission()
    {
        var (_, vehicle) = SaveReadyResources();
        var viewOnly = new DriverService(Db, User("view.only", PermissionCatalog.Drivers.View), Audit);
        var noDriverPermission = new VehicleService(Db, User("vehicles.only", PermissionCatalog.Vehicles.Manage), Audit);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            viewOnly.CreateCompositeAsync(NewDriver(vehicle.Id)));
        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            noDriverPermission.CreateCompositeAsync(new VehicleCompositeCreateRequest(VehicleRequest(), Guid.NewGuid())));

        Assert.Empty(Db.Drivers.AsNoTracking().Where(item => item.MobilePhone == "09120000001"));
        Assert.False(Db.Vehicles.AsNoTracking().Any(item => item.Brand == "پژو"));
    }

    [Fact]
    public async Task Vehicle_profile_update_keeps_unchanged_driver_without_relationship_permission()
    {
        var (driver, vehicle) = SaveReadyResources();
        driver.SetDefaultVehicle(vehicle.Id);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var service = new VehicleService(Db, User("vehicle.profile.only",
            PermissionCatalog.Vehicles.Manage, PermissionCatalog.Vehicles.View), Audit);
        var rowVersion = Db.Vehicles.AsNoTracking().Single(item => item.Id == vehicle.Id).RowVersion.ToRowVersionString();

        var saved = await service.UpdateCompositeAsync(vehicle.Id, EditVehicle("برند جدید", driver.Id, rowVersion));

        Assert.Equal("برند جدید", saved.Brand);
        Assert.Equal(vehicle.Id, Db.Drivers.AsNoTracking().Single(item => item.Id == driver.Id).DefaultVehicleId);
    }

    [Fact]
    public async Task Vehicle_profile_update_cannot_clear_driver_without_relationship_permission()
    {
        var (driver, vehicle) = SaveReadyResources();
        driver.SetDefaultVehicle(vehicle.Id);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var service = new VehicleService(Db, User("vehicle.profile.only",
            PermissionCatalog.Vehicles.Manage, PermissionCatalog.Vehicles.View), Audit);
        var rowVersion = Db.Vehicles.AsNoTracking().Single(item => item.Id == vehicle.Id).RowVersion.ToRowVersionString();

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            service.UpdateCompositeAsync(vehicle.Id, EditVehicle("برند جدید", null, rowVersion)));

        Db.ChangeTracker.Clear();
        var persisted = Db.Vehicles.AsNoTracking().Single(item => item.Id == vehicle.Id);
        var holder = Db.Drivers.AsNoTracking().Single(item => item.Id == driver.Id);
        Assert.Equal("ایویکو", persisted.Brand);
        Assert.Equal(vehicle.Id, holder.DefaultVehicleId);
    }

    [Fact]
    public async Task Stale_profile_row_version_rejects_driver_and_vehicle_composite_updates()
    {
        var (driver, _) = SaveReadyResources();
        var (_, vehicle) = SaveReadyResources();
        var options = new DbContextOptionsBuilder<NovaFleetDbContext>()
            .UseSqlite(Connection)
            .AddInterceptors(new SqliteRowVersionInterceptor())
            .ReplaceService<IModelCustomizer, ConcurrentSqliteModelCustomizer>()
            .Options;
        await using var concurrentDb = new NovaFleetDbContext(options);
        var drivers = new DriverService(concurrentDb, Admin(), Audit);
        var vehicles = new VehicleService(concurrentDb, Admin(), Audit);
        var stale = Convert.ToBase64String(new byte[8]);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            drivers.UpdateCompositeAsync(driver.Id, EditDriver("نام جدید", null, stale)));
        concurrentDb.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            vehicles.UpdateCompositeAsync(vehicle.Id, EditVehicle("برند جدید", null, stale)));
    }

    private sealed class ConcurrentSqliteModelCustomizer(ModelCustomizerDependencies dependencies)
        : SqliteProviderColumnTypesModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Driver>().Property(item => item.RowVersion).IsConcurrencyToken(true);
            modelBuilder.Entity<NovaFleet.Domain.Vehicles.Vehicle>().Property(item => item.RowVersion).IsConcurrencyToken(true);
        }
    }
}

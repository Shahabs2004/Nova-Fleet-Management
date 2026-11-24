using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Application.Vehicles;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Missions;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Security;
using NovaFleet.Infrastructure.Vehicles;

namespace NovaFleet.IntegrationTests;

public sealed class AllocationSqlFactAttribute : FactAttribute
{
    public AllocationSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVAFLEET_ALLOCATION_TEST_SQL")))
            Skip = "Requires the explicitly configured, isolated allocation SQL test database.";
    }
}

public sealed class AllocationSqlTheoryAttribute : TheoryAttribute
{
    public AllocationSqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVAFLEET_ALLOCATION_TEST_SQL")))
            Skip = "Requires the explicitly configured, isolated allocation SQL test database.";
    }
}

public sealed class MissionAllocationConcurrencyTests
{
    private const string DatabaseName = "NovaFleet_Allocation_Test_20261125";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly TimeSpan TehranOffset = TimeSpan.FromMinutes(210);

    [AllocationSqlFact]
    public async Task ExtensionCannotRaceAllocationIntoTheSameResources()
    {
        await EnsureDatabaseAsync();
        var clock = new MutableClock(Hour(0, 8));
        var settings = new TestSettings();
        settings.Values[SettingKeys.MinimumGapMinutes] = "0";
        var user = new TestUser();
        var (driverId, vehicleId) = await SeedResourcesAsync();
        Guid existingId;
        Guid pendingId;
        await using (var seed = Open())
        {
            existingId = await CreateReviewedAsync(seed, clock, settings, user, Hour(0, 10), Hour(0, 12));
            pendingId = await CreateReviewedAsync(seed, clock, settings, user, Hour(0, 11), Hour(0, 13));
            await MissionService(seed, clock, settings, user).ApproveAsync(existingId,
                new MissionApprovalRequest(driverId, vehicleId, null), Ct);
        }

        var pause = new PauseSaveInterceptor(existingId);
        await using var extensionDb = Open(pause);
        await using var allocationDb = Open();
        await RaceAsync(
            () => MissionService(extensionDb, clock, settings, user).ExtendAsync(existingId,
                new MissionExtensionRequest(Hour(0, 13), "تمدید هم‌زمان"), Ct),
            () => MissionService(allocationDb, clock, settings, user).ApproveAsync(pendingId,
                new MissionApprovalRequest(driverId, vehicleId, null), Ct),
            pause);

        await using var check = Open();
        var existing = await check.Missions.AsNoTracking().SingleAsync(item => item.Id == existingId, Ct);
        var pending = await check.Missions.AsNoTracking().SingleAsync(item => item.Id == pendingId, Ct);
        Assert.True((existing.Status == MissionStatus.Confirmed && pending.Status == MissionStatus.UnderReview)
            || (existing.PlannedEndAtUtc == Hour(0, 13) && pending.Status == MissionStatus.Confirmed));
    }

    [AllocationSqlFact]
    public async Task VehicleArchiveCannotRaceAllocation()
    {
        await EnsureDatabaseAsync();
        var clock = new MutableClock(Hour(0, 8));
        var settings = new TestSettings();
        settings.Values[SettingKeys.MinimumGapMinutes] = "0";
        var user = new TestUser();
        var (driverId, vehicleId) = await SeedResourcesAsync();
        Guid pendingId;
        await using (var seed = Open())
            pendingId = await CreateReviewedAsync(seed, clock, settings, user, Hour(0, 10), Hour(0, 12));

        var pause = new PauseSaveInterceptor(vehicleId);
        await using var vehicleDb = Open(pause);
        await using var allocationDb = Open();
        await RaceAsync(
            () => VehicleService(vehicleDb, user).SetArchivedAsync(vehicleId, true, Ct),
            () => MissionService(allocationDb, clock, settings, user).ApproveAsync(pendingId,
                new MissionApprovalRequest(driverId, vehicleId, null), Ct),
            pause);

        await using var check = Open();
        var vehicle = await check.Vehicles.AsNoTracking().SingleAsync(item => item.Id == vehicleId, Ct);
        var mission = await check.Missions.AsNoTracking().SingleAsync(item => item.Id == pendingId, Ct);
        Assert.True(!vehicle.IsArchived || mission.Status != MissionStatus.Confirmed);
    }

    [AllocationSqlTheory]
    [InlineData(ManualVehicleStatus.RepairShop)]
    [InlineData(ManualVehicleStatus.OutOfService)]
    public async Task VehicleManualStatusCannotRaceAllocation(ManualVehicleStatus status)
    {
        await EnsureDatabaseAsync();
        var clock = new MutableClock(Hour(0, 8));
        var settings = new TestSettings();
        settings.Values[SettingKeys.MinimumGapMinutes] = "0";
        var user = new TestUser();
        var (driverId, vehicleId) = await SeedResourcesAsync();
        Guid pendingId;
        await using (var seed = Open())
            pendingId = await CreateReviewedAsync(seed, clock, settings, user, Hour(0, 10), Hour(0, 12));

        var pause = new PauseSaveInterceptor(vehicleId);
        await using var vehicleDb = Open(pause);
        await using var allocationDb = Open();
        await RaceAsync(
            () => VehicleService(vehicleDb, user).SetManualStatusAsync(vehicleId, status, null, Ct),
            () => MissionService(allocationDb, clock, settings, user).ApproveAsync(pendingId,
                new MissionApprovalRequest(driverId, vehicleId, null), Ct),
            pause);

        await using var check = Open();
        var vehicle = await check.Vehicles.AsNoTracking().SingleAsync(item => item.Id == vehicleId, Ct);
        var mission = await check.Missions.AsNoTracking().SingleAsync(item => item.Id == pendingId, Ct);
        Assert.True(vehicle.ManualStatus == ManualVehicleStatus.Ready || mission.Status != MissionStatus.Confirmed);
    }

    [AllocationSqlFact]
    public async Task ConcurrentAllocationsCannotBothUseTheSameResources()
    {
        await EnsureDatabaseAsync();
        var clock = new MutableClock(Hour(0, 8));
        var settings = new TestSettings();
        settings.Values[SettingKeys.MinimumGapMinutes] = "0";
        var user = new TestUser();
        var (driverId, vehicleId) = await SeedResourcesAsync();
        Guid firstId;
        Guid secondId;
        await using (var seed = Open())
        {
            firstId = await CreateReviewedAsync(seed, clock, settings, user, Hour(0, 10), Hour(0, 12));
            secondId = await CreateReviewedAsync(seed, clock, settings, user, Hour(0, 10), Hour(0, 12));
        }

        await using var firstDb = Open();
        await using var secondDb = Open();
        var errors = await Task.WhenAll(
            CaptureAsync(() => MissionService(firstDb, clock, settings, user).ApproveAsync(firstId,
                new MissionApprovalRequest(driverId, vehicleId, null), Ct)),
            CaptureAsync(() => MissionService(secondDb, clock, settings, user).ApproveAsync(secondId,
                new MissionApprovalRequest(driverId, vehicleId, null), Ct)));

        Assert.Equal(1, errors.Count(error => error is null));
        Assert.All(errors, error => Assert.True(error is null or ConflictAppException, error?.ToString()));
        await using var check = Open();
        Assert.Equal(1, await check.Missions.CountAsync(item => item.DriverId == driverId
            && item.Status == MissionStatus.Confirmed, Ct));
    }

    private static async Task RaceAsync(Func<Task> firstOperation, Func<Task> secondOperation, PauseSaveInterceptor pause)
    {
        var first = CaptureAsync(firstOperation);
        try
        {
            await Task.WhenAny(pause.Started.Task, first).WaitAsync(TimeSpan.FromSeconds(15));
            if (first.IsCompleted)
                await first;
            Assert.True(pause.Started.Task.IsCompleted, "The first operation did not reach its save point.");

            var second = CaptureAsync(secondOperation);
            var finishedEarly = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(1)));
            var blocked = finishedEarly != second;
            pause.Release();
            var errors = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(blocked, "The conflicting operation completed while the first resource lock was held.");
            Assert.Equal(1, errors.Count(error => error is null));
            Assert.All(errors, error => Assert.True(error is null or ConflictAppException, error?.ToString()));
        }
        finally
        {
            pause.Release();
        }
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static async Task<(Guid Driver, Guid Vehicle)> SeedResourcesAsync()
    {
        await using var db = Open();
        var driver = new Driver("راننده", "آزمون", "09" + Random.Shared.Next(100000000, 999999999));
        var plate = IranianLicensePlate.Create(
            Random.Shared.Next(10, 100).ToString(),
            "ب",
            Random.Shared.Next(100, 1000).ToString(),
            Random.Shared.Next(10, 100).ToString());
        var vehicle = new Vehicle("آزمون", "هم‌زمانی", plate);
        db.Drivers.Add(driver);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(Ct);
        return (driver.Id, vehicle.Id);
    }

    private static async Task<Guid> CreateReviewedAsync(
        NovaFleetDbContext db,
        MutableClock clock,
        TestSettings settings,
        TestUser user,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var service = MissionService(db, clock, settings, user);
        var created = await service.CreateAsync(new MissionRequestInput(
            Title: "مأموریت هم‌زمانی",
            Type: MissionType.Freight,
            Purpose: null,
            Origin: "مبدأ",
            IntermediateStops: [],
            Destination: "مقصد",
            PlannedStartAtUtc: start,
            PlannedEndAtUtc: end,
            RequesterPhoneSnapshot: "09120000000",
            RequesterOrganizationalUnit: "آزمون",
            Description: null,
            PassengerCount: null,
            MissionOrderNumber: null,
            MissionOrderDate: null,
            CargoDescription: null,
            ConsigneeName: null,
            PassengerNames: null), Ct);
        await service.StartReviewAsync(created.Id, Ct);
        return created.Id;
    }

    private static MissionService MissionService(NovaFleetDbContext db, MutableClock clock, TestSettings settings, TestUser user) =>
        new(db, user, new NoopAudit(), clock, settings, new NoopNotifications(), new NoopSms());

    private static VehicleService VehicleService(NovaFleetDbContext db, TestUser user) => new(db, user, new NoopAudit());

    private static DateTimeOffset Hour(int day, int hour) =>
        new DateTimeOffset(2026, 11, 25, 0, 0, 0, TehranOffset).AddDays(day).AddHours(hour);

    private static async Task EnsureDatabaseAsync()
    {
        var connection = TestConnection();
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync(Ct);
        var command = master.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{DatabaseName}') IS NULL CREATE DATABASE [{DatabaseName}]";
        await command.ExecuteNonQueryAsync(Ct);
        await using var db = Open();
        await db.Database.MigrateAsync(Ct);
    }

    private static string TestConnection()
    {
        var connection = Environment.GetEnvironmentVariable("NOVAFLEET_ALLOCATION_TEST_SQL")
            ?? throw new InvalidOperationException("An isolated allocation SQL connection is required.");
        var parsed = new SqlConnectionStringBuilder(connection);
        if (!string.Equals(parsed.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase)
            || parsed.InitialCatalog != DatabaseName
            || !parsed.IntegratedSecurity
            || !string.IsNullOrWhiteSpace(parsed.UserID)
            || !string.IsNullOrWhiteSpace(parsed.Password)
            || !string.IsNullOrWhiteSpace(parsed.AttachDBFilename))
        {
            throw new InvalidOperationException("Only integrated-security LocalDB with the approved allocation test catalog is allowed.");
        }
        return connection;
    }

    private static NovaFleetDbContext Open(SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<NovaFleetDbContext>().UseSqlServer(TestConnection());
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new NovaFleetDbContext(options.Options);
    }

    private sealed class TestUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string Username { get; } = "allocation.test";
        public string DisplayName => "آزمون هم‌زمانی";
        public bool IsLocalAdmin => false;
        public EffectivePermissionSet Permissions { get; } = CreatePermissions();

        private static EffectivePermissionSet CreatePermissions()
        {
            string[] permissions = [PermissionCatalog.Missions.Create, PermissionCatalog.Missions.Review,
                PermissionCatalog.Missions.Approve, PermissionCatalog.Missions.Extend,
                PermissionCatalog.Vehicles.Manage, PermissionCatalog.Vehicles.View];
            return EffectivePermissionSet.Build(permissions.Select(permission => (permission, PermissionEffect.Grant)));
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class TestSettings : ISettingsService
    {
        public Dictionary<string, string?> Values { get; } = [];
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.GetValueOrDefault(key));
        public Task<SettingsBundleDto> GetBundleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SettingsBundleDto> GetRuntimeBundleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrganizationProfileDto> UpdateOrganizationAsync(OrganizationProfileDto profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MissionRulesDto> UpdateMissionRulesAsync(MissionRulesDto rules, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionPolicyDto> UpdateSessionPolicyAsync(SessionPolicyDto policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MissionOrderTemplateDto> UpdateMissionOrderTemplateAsync(MissionOrderTemplateDto template, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SplitReasonDto>> ListSplitReasonsAsync(bool includeInactive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SplitReasonDto> CreateSplitReasonAsync(string title, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SplitReasonDto> UpdateSplitReasonAsync(Guid id, UpdateSplitReasonRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopAudit : IAuditTrail
    {
        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopNotifications : INotificationPublisher
    {
        public Task PublishAsync(NotificationMessage message, IEnumerable<string> usernames, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishToPermissionAsync(NotificationMessage message, string permission, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishToRolesAsync(NotificationMessage message, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopSms : ISmsDispatcher
    {
        public Task QueueForMissionAsync(Guid missionId, SmsEventType eventType, string? reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RescheduleStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> ProcessQueueAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> PollDeliveryAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> RecoverStaleSendsAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class PauseSaveInterceptor(Guid entityId) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => release.TrySetResult();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var tracked = eventData.Context!.ChangeTracker;
            var target = tracked.Entries<Mission>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == entityId)
                || tracked.Entries<Vehicle>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == entityId);
            if (target)
            {
                Started.TrySetResult();
                return WaitAsync(result, cancellationToken);
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);

            async ValueTask<InterceptionResult<int>> WaitAsync(InterceptionResult<int> current, CancellationToken token)
            {
                await release.Task.WaitAsync(token);
                return await base.SavingChangesAsync(eventData, current, token);
            }
        }
    }
}

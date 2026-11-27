using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Missions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.IntegrationTests;

public sealed class MissionChangeSqlFactAttribute : FactAttribute
{
    public MissionChangeSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVAFLEET_MISSION_CHANGE_TEST_SQL")))
            Skip = "Requires the explicitly configured, isolated mission change SQL test database.";
    }
}

/// <summary>
/// آزمون مهاجرت snapshot درخواست تغییر فقط روی LocalDB و کاتالوگ اختصاصی Stage 6 اجرا می‌شود.
/// رشته اتصال تولیدی، فایل پیوست و احراز هویت SQL پذیرفته نمی‌شوند.
/// </summary>
public sealed class MissionChangeSnapshotMigrationIntegrationTests : IAsyncLifetime
{
    private const string DataSource = @"(localdb)\MSSQLLocalDB";
    private const string Catalog = "NovaFleet_MissionChangeSnapshot_Test_20261124";
    private const string EnvironmentVariable = "NOVAFLEET_MISSION_CHANGE_TEST_SQL";
    private const string PreviousMigration = "20261124011343_AddSmsQueueSafety";

    private string connectionString = null!;
    private DbContextOptions<NovaFleetDbContext> options = null!;
    private NovaFleetDbContext db = null!;

    public async Task InitializeAsync()
    {
        connectionString = ReadGuardedConnectionString();
        await RecreateCatalogAsync();
        options = new DbContextOptionsBuilder<NovaFleetDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        db = new NovaFleetDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (db is not null)
        {
            await db.DisposeAsync();
        }

        SqlConnection.ClearAllPools();
        await DropCatalogAsync();
    }

    [MissionChangeSqlFact]
    public async Task Schema_BaseSnapshotColumnsAreNullableAndBaseRowVersionIsPlainVarbinaryEight()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        Assert.Equal("varbinary", await ScalarAsync<string>(connection, ColumnTypeSql("BaseRowVersion")));
        Assert.Equal((short)8, await ScalarAsync<short>(connection, ColumnLengthSql("BaseRowVersion")));
        Assert.True(await ScalarAsync<bool>(connection, ColumnNullableSql("BaseRowVersion")));
        Assert.False(await ScalarAsync<bool>(connection, ColumnComputedSql("BaseRowVersion")));

        Assert.Equal("int", await ScalarAsync<string>(connection, ColumnTypeSql("BaseStatus")));
        Assert.True(await ScalarAsync<bool>(connection, ColumnNullableSql("BaseStatus")));
        Assert.Equal("datetimeoffset", await ScalarAsync<string>(connection, ColumnTypeSql("BasePlannedStartAtUtc")));
        Assert.True(await ScalarAsync<bool>(connection, ColumnNullableSql("BasePlannedStartAtUtc")));
        Assert.Equal("datetimeoffset", await ScalarAsync<string>(connection, ColumnTypeSql("BasePlannedEndAtUtc")));
        Assert.True(await ScalarAsync<bool>(connection, ColumnNullableSql("BasePlannedEndAtUtc")));
    }

    [MissionChangeSqlFact]
    public async Task Persistence_CopiedSnapshotDoesNotChangeWhenMissionRowVersionAdvances()
    {
        var now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.FromMinutes(210));
        var mission = NewMission(now);
        db.Missions.Add(mission);
        await db.SaveChangesAsync();
        Assert.Equal(8, mission.RowVersion.Length);
        var expectedSnapshot = mission.RowVersion.ToArray();
        var change = new MissionChangeRequest(
            mission.Id,
            "stage6.tester",
            "بررسی snapshot",
            "{}",
            new MissionChangeRequestSnapshot(
                expectedSnapshot,
                mission.Status,
                mission.PlannedStartAtUtc,
                mission.PlannedEndAtUtc));
        db.MissionChangeRequests.Add(change);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Missions] SET [Title] = N'عنوان بعد از درخواست' WHERE [Id] = {mission.Id}");
        db.ChangeTracker.Clear();

        using var verification = CreateContext();
        var storedMission = await verification.Missions.AsNoTracking().SingleAsync(item => item.Id == mission.Id);
        var storedChange = await verification.MissionChangeRequests.AsNoTracking().SingleAsync(item => item.Id == change.Id);
        Assert.Equal(8, storedMission.RowVersion.Length);
        Assert.False(expectedSnapshot.AsSpan().SequenceEqual(storedMission.RowVersion));
        Assert.NotNull(storedChange.BaseRowVersion);
        Assert.True(expectedSnapshot.AsSpan().SequenceEqual(storedChange.BaseRowVersion));
        Assert.Equal(mission.Status, storedChange.BaseStatus);
        Assert.Equal(mission.PlannedStartAtUtc, storedChange.BasePlannedStartAtUtc);
        Assert.Equal(mission.PlannedEndAtUtc, storedChange.BasePlannedEndAtUtc);
    }

    [MissionChangeSqlFact]
    public async Task Decision_WhenChangeRequestIsLocked_IsConflictWithoutMutation()
    {
        var now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.FromMinutes(210));
        var mission = NewMission(now);
        var driver = new Driver("راننده", "آزمون", "09121112233");
        var vehicle = new Vehicle("ایویکو", "دیلی", IranianLicensePlate.Create("12", "ب", "345", "40"));
        db.Missions.Add(mission);
        db.Drivers.Add(driver);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        mission.Approve(driver.Id, vehicle.Id, "stage6.reviewer", now);
        await db.SaveChangesAsync();

        var proposed = new MissionChangeInput(
            mission.Title,
            mission.Type,
            mission.Purpose,
            mission.Origin,
            mission.GetIntermediateStops(),
            mission.Destination,
            mission.PlannedStartAtUtc,
            mission.PlannedEndAtUtc,
            mission.RequesterPhoneSnapshot,
            mission.Description,
            mission.PassengerCount,
            mission.MissionOrderNumber,
            mission.MissionOrderDate,
            mission.CargoDescription,
            mission.ConsigneeName,
            mission.PassengerNames,
            driver.Id,
            vehicle.Id,
            null);
        var change = new MissionChangeRequest(
            mission.Id,
            mission.RequesterUsername,
            "آزمون قفل هم‌زمان",
            JsonSerializer.Serialize(proposed, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new MissionChangeRequestSnapshot(
                mission.RowVersion,
                mission.Status,
                mission.PlannedStartAtUtc,
                mission.PlannedEndAtUtc));
        change.StartReview("stage6.reviewer", now);
        db.MissionChangeRequests.Add(change);
        await db.SaveChangesAsync();

        await using var blocker = new SqlConnection(connectionString);
        await blocker.OpenAsync();
        await using var blockerTx = await blocker.BeginTransactionAsync();
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = (SqlTransaction)blockerTx;
            lockCommand.CommandText = "UPDATE [MissionChangeRequests] SET [Reason] = [Reason] WHERE [Id] = @id";
            lockCommand.Parameters.Add(new SqlParameter("@id", change.Id));
            await lockCommand.ExecuteNonQueryAsync();
        }

        var audit = new FakeAuditTrail();
        var sms = new FakeSmsDispatcher();
        await using var serviceDb = CreateContext();
        await serviceDb.Database.OpenConnectionAsync();
        await serviceDb.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 1000;");
        var service = new MissionService(
            serviceDb,
            new TestCurrentUser(),
            audit,
            new FixedClock(now),
            new FakeSettingsService(),
            new FakeNotifications(),
            sms);
        try
        {
            await Assert.ThrowsAsync<ConflictAppException>(() => service.DecideChangeAsync(
                mission.Id, change.Id, approve: true, new MissionChangeDecision("موافقت")));
        }
        finally
        {
            await blockerTx.RollbackAsync();
        }

        Assert.Empty(audit.Records);
        Assert.Equal(0, sms.Queued);
        using var verification = CreateContext();
        var storedMission = await verification.Missions.AsNoTracking().SingleAsync(item => item.Id == mission.Id);
        var storedChange = await verification.MissionChangeRequests.AsNoTracking().SingleAsync(item => item.Id == change.Id);
        Assert.Equal(MissionStatus.Confirmed, storedMission.Status);
        Assert.Equal(driver.Id, storedMission.DriverId);
        Assert.Equal(vehicle.Id, storedMission.VehicleId);
        Assert.Equal(MissionChangeRequestStatus.UnderReview, storedChange.Status);
        Assert.Null(storedChange.ReviewedAtUtc);
    }

    [MissionChangeSqlFact]
    public async Task Decision_WhenMissionUpdateRaisesLockTimeout_IsConflictWithoutMutationOrSideEffects()
    {
        var now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.FromMinutes(210));
        var mission = NewMission(now);
        var driver = new Driver("راننده", "آزمون", "09121112233");
        var vehicle = new Vehicle("ایویکو", "دیلی", IranianLicensePlate.Create("12", "ب", "345", "40"));
        db.Missions.Add(mission);
        db.Drivers.Add(driver);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        mission.Approve(driver.Id, vehicle.Id, "stage6.reviewer", now);
        await db.SaveChangesAsync();

        var proposed = new MissionChangeInput(
            mission.Title + " ویرایش",
            mission.Type,
            mission.Purpose,
            mission.Origin,
            mission.GetIntermediateStops(),
            mission.Destination,
            mission.PlannedStartAtUtc,
            mission.PlannedEndAtUtc,
            mission.RequesterPhoneSnapshot,
            mission.Description,
            mission.PassengerCount,
            mission.MissionOrderNumber,
            mission.MissionOrderDate,
            mission.CargoDescription,
            mission.ConsigneeName,
            mission.PassengerNames,
            driver.Id,
            vehicle.Id,
            null);
        var change = new MissionChangeRequest(
            mission.Id,
            mission.RequesterUsername,
            "آزمون خطای ذخیره",
            JsonSerializer.Serialize(proposed, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new MissionChangeRequestSnapshot(
                mission.RowVersion,
                mission.Status,
                mission.PlannedStartAtUtc,
                mission.PlannedEndAtUtc));
        change.StartReview("stage6.reviewer", now);
        db.MissionChangeRequests.Add(change);
        await db.SaveChangesAsync();

        var serviceOptions = new DbContextOptionsBuilder<NovaFleetDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new MissionUpdateLockTimeoutInterceptor(connectionString))
            .Options;
        var audit = new FakeAuditTrail();
        var sms = new FakeSmsDispatcher();
        await using var serviceDb = new NovaFleetDbContext(serviceOptions);
        var service = new MissionService(
            serviceDb,
            new TestCurrentUser(),
            audit,
            new FixedClock(now),
            new FakeSettingsService(),
            new FakeNotifications(),
            sms);

        await Assert.ThrowsAsync<ConflictAppException>(() => service.DecideChangeAsync(
            mission.Id, change.Id, approve: true, new MissionChangeDecision("موافقت")));

        Assert.Empty(audit.Records);
        Assert.Equal(0, sms.Queued);
        using var verification = CreateContext();
        var storedMission = await verification.Missions.AsNoTracking().SingleAsync(item => item.Id == mission.Id);
        var storedChange = await verification.MissionChangeRequests.AsNoTracking().SingleAsync(item => item.Id == change.Id);
        Assert.Equal("مأموریت آزمون Stage 6", storedMission.Title);
        Assert.Equal(MissionStatus.Confirmed, storedMission.Status);
        Assert.Equal(driver.Id, storedMission.DriverId);
        Assert.Equal(vehicle.Id, storedMission.VehicleId);
        Assert.Equal(MissionChangeRequestStatus.UnderReview, storedChange.Status);
        Assert.Null(storedChange.ReviewedAtUtc);
    }

    [MissionChangeSqlFact]
    public async Task UpgradeAndDowngrade_LegacyPendingRequestKeepsDataAndGetsStaleNullableSnapshot()
    {
        var now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.FromMinutes(210));
        var mission = NewMission(now);
        db.Missions.Add(mission);
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync(PreviousMigration);
        db.ChangeTracker.Clear();
        var changeId = Guid.NewGuid();
        const string proposedJson = "{\"title\":\"legacy proposal\"}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [MissionChangeRequests]
                ([Id], [MissionId], [RequesterUsername], [Reason], [ProposedValuesJson], [Status], [CreatedAtUtc])
            VALUES
                ({changeId}, {mission.Id}, {"stage6.tester"}, {"legacy request"}, {proposedJson},
                 {(int)MissionChangeRequestStatus.Pending}, {now});
            """);

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        using (var verification = CreateContext())
        {
            var stored = await verification.MissionChangeRequests.AsNoTracking().SingleAsync(item => item.Id == changeId);
            Assert.Equal(MissionChangeRequestStatus.Pending, stored.Status);
            Assert.Equal("legacy request", stored.Reason);
            Assert.Equal(proposedJson, stored.ProposedValuesJson);
            Assert.Null(stored.BaseRowVersion);
            Assert.Null(stored.BaseStatus);
            Assert.Null(stored.BasePlannedStartAtUtc);
            Assert.Null(stored.BasePlannedEndAtUtc);
        }

        try
        {
            await db.Database.MigrateAsync(PreviousMigration);
            db.ChangeTracker.Clear();
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            Assert.Null(await ScalarAsync<int?>(connection, "SELECT COL_LENGTH('dbo.MissionChangeRequests', 'BaseRowVersion')"));
            Assert.Null(await ScalarAsync<int?>(connection, "SELECT COL_LENGTH('dbo.MissionChangeRequests', 'BaseStatus')"));
            Assert.Null(await ScalarAsync<int?>(connection, "SELECT COL_LENGTH('dbo.MissionChangeRequests', 'BasePlannedStartAtUtc')"));
            Assert.Null(await ScalarAsync<int?>(connection, "SELECT COL_LENGTH('dbo.MissionChangeRequests', 'BasePlannedEndAtUtc')"));
            Assert.Equal((int)MissionChangeRequestStatus.Pending, await ScalarAsync<int>(connection,
                "SELECT [Status] FROM [MissionChangeRequests] WHERE [Id] = @id", new SqlParameter("@id", changeId)));
            Assert.Equal(proposedJson, await ScalarAsync<string>(connection,
                "SELECT [ProposedValuesJson] FROM [MissionChangeRequests] WHERE [Id] = @id", new SqlParameter("@id", changeId)));
        }
        finally
        {
            await db.Database.MigrateAsync();
        }
    }

    private sealed class MissionUpdateLockTimeoutInterceptor(string connectionString) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE [Missions]", StringComparison.OrdinalIgnoreCase))
            {
                await using var blocker = new SqlConnection(connectionString);
                await blocker.OpenAsync(cancellationToken);
                await using var lockCommand = blocker.CreateCommand();
                lockCommand.CommandText = "SET LOCK_TIMEOUT 100; SELECT TOP (1) [Id] FROM [Missions] WITH (TABLOCKX);";
                try
                {
                    await lockCommand.ExecuteReaderAsync(cancellationToken);
                }
                catch (SqlException ex) when (ex.Number == 1222)
                {
                    throw new DbUpdateException("Simulated save-time lock timeout.", ex);
                }

                throw new InvalidOperationException("Expected the mission update boundary to encounter a lock timeout.");
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string Username => "stage6.reviewer";
        public string DisplayName => "بازبین آزمون";
        public bool IsLocalAdmin => false;
        public EffectivePermissionSet Permissions { get; } = EffectivePermissionSet.Build(
            [(PermissionCatalog.Missions.ChangeReview, PermissionEffect.Grant)]);
    }

    private sealed class FixedClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset UtcNow => nowUtc;
    }

    private sealed class FakeAuditTrail : IAuditTrail
    {
        public List<AuditRecord> Records { get; } = [];

        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<SettingsBundleDto> GetRuntimeBundleAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsBundleDto(
                new OrganizationProfileDto("ناوگان آزمون", null),
                new MissionRulesDto(30, 30, 24, 30, null),
                new SessionPolicyDto(480, 60),
                new MissionOrderTemplateDto(null, null, false)));

        public Task<SettingsBundleDto> GetBundleAsync(CancellationToken cancellationToken = default) =>
            GetRuntimeBundleAsync(cancellationToken);

        public Task<OrganizationProfileDto> UpdateOrganizationAsync(OrganizationProfileDto profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MissionRulesDto> UpdateMissionRulesAsync(MissionRulesDto rules, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SessionPolicyDto> UpdateSessionPolicyAsync(SessionPolicyDto policy, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MissionOrderTemplateDto> UpdateMissionOrderTemplateAsync(MissionOrderTemplateDto template, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SplitReasonDto>> ListSplitReasonsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SplitReasonDto> CreateSplitReasonAsync(string title, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SplitReasonDto> UpdateSplitReasonAsync(Guid id, UpdateSplitReasonRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeNotifications : INotificationPublisher
    {
        public Task PublishAsync(NotificationMessage message, IEnumerable<string> usernames, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishToPermissionAsync(NotificationMessage message, string permission, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeSmsDispatcher : ISmsDispatcher
    {
        public int Queued { get; private set; }

        public Task QueueForMissionAsync(Guid missionId, SmsEventType eventType, string? reason, CancellationToken cancellationToken = default)
        {
            Queued++;
            return Task.CompletedTask;
        }

        public Task RescheduleStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> ProcessQueueAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> PollDeliveryAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> RecoverStaleSendsAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private static Mission NewMission(DateTimeOffset now) => new(
        $"SN-{Guid.NewGuid():N}"[..20],
        "مأموریت آزمون Stage 6",
        "stage6.tester",
        "آزمونگر Stage 6",
        "09121112233",
        "واحد آزمون",
        MissionType.Freight,
        null,
        "شیراز",
        "بندرعباس",
        null,
        now.AddHours(2),
        now.AddHours(4));

    private NovaFleetDbContext CreateContext()
    {
        _ = ReadGuardedConnectionString(connectionString);
        return new NovaFleetDbContext(options);
    }

    private static string ColumnTypeSql(string column) => $"""
        SELECT TYPE_NAME([c].[user_type_id])
        FROM [sys].[columns] AS [c]
        JOIN [sys].[tables] AS [t] ON [t].[object_id] = [c].[object_id]
        WHERE [t].[name] = N'MissionChangeRequests' AND [c].[name] = N'{column}';
        """;

    private static string ColumnLengthSql(string column) => $"""
        SELECT [c].[max_length]
        FROM [sys].[columns] AS [c]
        JOIN [sys].[tables] AS [t] ON [t].[object_id] = [c].[object_id]
        WHERE [t].[name] = N'MissionChangeRequests' AND [c].[name] = N'{column}';
        """;

    private static string ColumnNullableSql(string column) => $"""
        SELECT [c].[is_nullable]
        FROM [sys].[columns] AS [c]
        JOIN [sys].[tables] AS [t] ON [t].[object_id] = [c].[object_id]
        WHERE [t].[name] = N'MissionChangeRequests' AND [c].[name] = N'{column}';
        """;

    private static string ColumnComputedSql(string column) => $"""
        SELECT [c].[is_computed]
        FROM [sys].[columns] AS [c]
        JOIN [sys].[tables] AS [t] ON [t].[object_id] = [c].[object_id]
        WHERE [t].[name] = N'MissionChangeRequests' AND [c].[name] = N'{column}';
        """;

    private static string ReadGuardedConnectionString(string? candidate = null)
    {
        var value = candidate ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Set {EnvironmentVariable} for this test process to the dedicated LocalDB mission change test database.");
        }

        var builder = new SqlConnectionStringBuilder(value);
        if (!string.Equals(builder.DataSource, DataSource, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Mission change SQL tests only allow (localdb)\\MSSQLLocalDB.");
        }

        if (!string.Equals(builder.InitialCatalog, Catalog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Mission change SQL tests only allow the dedicated catalog {Catalog}.");
        }

        if (!builder.IntegratedSecurity || !string.IsNullOrWhiteSpace(builder.UserID)
            || !string.IsNullOrWhiteSpace(builder.Password) || !string.IsNullOrWhiteSpace(builder.AttachDBFilename))
        {
            throw new InvalidOperationException("Mission change SQL tests require integrated Windows authentication without credentials or attached files.");
        }

        return builder.ConnectionString;
    }

    private static async Task RecreateCatalogAsync()
    {
        var builder = new SqlConnectionStringBuilder(ReadGuardedConnectionString())
        {
            InitialCatalog = "master",
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var exists = await ScalarAsync<int?>(connection,
            "SELECT DB_ID(@name)", new SqlParameter("@name", Catalog));
        if (exists is not null)
        {
            await ExecuteAsync(connection,
                $"ALTER DATABASE [{Catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Catalog}];");
        }

        await ExecuteAsync(connection, $"CREATE DATABASE [{Catalog}];");
    }

    private static async Task DropCatalogAsync()
    {
        var builder = new SqlConnectionStringBuilder(ReadGuardedConnectionString())
        {
            InitialCatalog = "master",
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var exists = await ScalarAsync<int?>(connection,
            "SELECT DB_ID(@name)", new SqlParameter("@name", Catalog));
        if (exists is not null)
        {
            await ExecuteAsync(connection,
                $"ALTER DATABASE [{Catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Catalog}];");
        }
    }

    private static async Task<T?> ScalarAsync<T>(DbConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null || value == DBNull.Value)
        {
            return default;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.IntegrationTests;

public sealed class MissionMigrationSqlFactAttribute : FactAttribute
{
    public MissionMigrationSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVAFLEET_MIGRATION_TEST_SQL")))
            Skip = "Requires the explicitly configured, isolated LocalDB migration test database.";
    }
}

/// <summary>
/// آزمون حفظ گروه درخواست‌کننده فقط روی LocalDB و کاتالوگی تازه و مطابق الگوی مجاز اجرا می‌شود.
/// </summary>
public sealed class MissionRequestingGroupMigrationTests : IAsyncLifetime
{
    private const string DataSource = @"(localdb)\MSSQLLocalDB";
    private const string EnvironmentVariable = "NOVAFLEET_MIGRATION_TEST_SQL";
    private const string PreDropMigration = "20261113174313_AddUserAndDriverAvatars";
    private const string OriginalDropMigration = "20261115091010_DropMissionRequestingGroupOptionalPurpose";
    private const string CompatibilityMigration = "20261123090000_EnsureMissionRequestingGroupArchive";
    private const string StageFiveMigration = "20261124011343_AddSmsQueueSafety";
    private static readonly Regex CatalogPattern = new(@"^NovaFleet_Migration_Test_[a-f0-9]{32}$");

    private string connectionString = null!;
    private string catalog = null!;
    private NovaFleetDbContext db = null!;

    public async Task InitializeAsync()
    {
        connectionString = CreateGuardedConnectionString();
        catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        await RecreateCatalogAsync();
        db = new NovaFleetDbContext(new DbContextOptionsBuilder<NovaFleetDbContext>()
            .UseSqlServer(connectionString)
            .Options);
    }

    public async Task DisposeAsync()
    {
        if (db is not null)
            await db.DisposeAsync();

        SqlConnection.ClearAllPools();
        if (!string.IsNullOrWhiteSpace(catalog))
            await DropCatalogAsync();
    }

    [MissionMigrationSqlFact]
    public async Task FreshPreDrop_ArchivesNonNullGroups_AndRollbackRestoresAvailableValues()
    {
        await db.Database.MigrateAsync(PreDropMigration);
        await ExecuteAsync("""
            ALTER TABLE dbo.Missions ALTER COLUMN RequestingGroup nvarchar(200) NULL;
            ALTER TABLE dbo.Missions ALTER COLUMN Purpose nvarchar(500) NULL;
            """);

        var first = Guid.NewGuid();
        var nullGroup = Guid.NewGuid();
        var third = Guid.NewGuid();
        await InsertMissionAsync(first, "M001", "واحد الف", "هدف اول", includeGroup: true);
        await InsertMissionAsync(nullGroup, "M002", null, null, includeGroup: true);
        await InsertMissionAsync(third, "M003", "واحد ج", null, includeGroup: true);

        await db.Database.MigrateAsync();

        var archive = await ReadArchiveAsync();
        Assert.Equal(2, archive.Count);
        Assert.Equal("واحد الف", archive[first]);
        Assert.Equal("واحد ج", archive[third]);
        Assert.False(archive.ContainsKey(nullGroup));
        Assert.True(await TableExistsAsync("MissionRequestingGroupArchive"));

        await ExecuteAsync("UPDATE dbo.Missions SET Purpose = NULL;");
        await db.Database.MigrateAsync(PreDropMigration);

        var missions = await ReadMissionStateAsync();
        Assert.Equal("واحد الف", missions[first].Group);
        Assert.Equal("واحد ج", missions[third].Group);
        Assert.Equal("", missions[nullGroup].Group);
        Assert.All(missions.Values, mission => Assert.Equal("", mission.Purpose));
        Assert.False(await TableExistsAsync("MissionRequestingGroupArchive"));
        Assert.False(await ColumnNullableAsync("RequestingGroup"));
        Assert.False(await ColumnNullableAsync("Purpose"));
    }

    [MissionMigrationSqlFact]
    public async Task AlreadyOldDropped_CreatesEmptyArchive_AndRollbackDoesNotInventLostGroup()
    {
        await db.Database.MigrateAsync(PreDropMigration);
        var missionId = Guid.NewGuid();
        await InsertMissionAsync(missionId, "M010", "گروه از دست رفته", "هدف باقی‌مانده", includeGroup: true);

        await ExecuteAsync("""
            ALTER TABLE dbo.Missions DROP COLUMN RequestingGroup;
            ALTER TABLE dbo.Missions ALTER COLUMN Purpose nvarchar(500) NULL;
            """);
        await ExecuteAsync("""
            INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
            VALUES (@migrationId, N'10.0.14');
            """, new SqlParameter("@migrationId", OriginalDropMigration));

        await db.Database.MigrateAsync();

        Assert.True(await TableExistsAsync("MissionRequestingGroupArchive"));
        Assert.Empty(await ReadArchiveAsync());
        Assert.Contains(CompatibilityMigration, await db.Database.GetAppliedMigrationsAsync());

        await db.Database.MigrateAsync(PreDropMigration);

        var mission = (await ReadMissionStateAsync())[missionId];
        Assert.Equal("", mission.Group);
        Assert.Equal("هدف باقی‌مانده", mission.Purpose);
        Assert.False(await TableExistsAsync("MissionRequestingGroupArchive"));
    }

    [MissionMigrationSqlFact]
    public async Task ColumnStillPresent_CopiesNonNullGroups_AndRollbackRestoresThem()
    {
        await db.Database.MigrateAsync(PreDropMigration);
        await ExecuteAsync("""
            ALTER TABLE dbo.Missions ALTER COLUMN RequestingGroup nvarchar(200) NULL;
            ALTER TABLE dbo.Missions ALTER COLUMN Purpose nvarchar(500) NULL;
            """);

        var preserved = Guid.NewGuid();
        var nullGroup = Guid.NewGuid();
        await InsertMissionAsync(preserved, "M030", "واحد د", null, includeGroup: true);
        await InsertMissionAsync(nullGroup, "M031", null, null, includeGroup: true);

        // Repairs a partial/legacy history where the drop is recorded but its column still exists.
        await ExecuteAsync("""
            INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
            VALUES (@migrationId, N'10.0.14');
            """, new SqlParameter("@migrationId", OriginalDropMigration));

        await db.Database.MigrateAsync();

        var archive = await ReadArchiveAsync();
        Assert.Single(archive);
        Assert.Equal("واحد د", archive[preserved]);

        await db.Database.MigrateAsync(PreDropMigration);

        var missions = await ReadMissionStateAsync();
        Assert.Equal("واحد د", missions[preserved].Group);
        Assert.Equal("", missions[nullGroup].Group);
        Assert.All(missions.Values, mission => Assert.Equal("", mission.Purpose));
        Assert.False(await TableExistsAsync("MissionRequestingGroupArchive"));
    }

    [MissionMigrationSqlFact]
    public async Task StageTwoAndFiveHistoryWithCompatibilityPending_AppliesConditionalArchiveBeforeHead()
    {
        await db.Database.MigrateAsync(StageFiveMigration);
        var missionId = Guid.NewGuid();
        await InsertMissionAsync(missionId, "M020", null, null, includeGroup: false);

        // Simulates an installation that ran later migrations before this compatibility migration existed.
        await ExecuteAsync("DROP TABLE dbo.MissionRequestingGroupArchive;");
        await ExecuteAsync(
            "DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId;",
            new SqlParameter("@migrationId", CompatibilityMigration));
        var appliedBefore = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20261123201500_AddLoginLockout", appliedBefore);
        Assert.Contains(StageFiveMigration, appliedBefore);
        Assert.DoesNotContain(CompatibilityMigration, appliedBefore);

        await db.Database.MigrateAsync();

        Assert.True(await TableExistsAsync("MissionRequestingGroupArchive"));
        Assert.Empty(await ReadArchiveAsync());
        Assert.Contains(CompatibilityMigration, await db.Database.GetAppliedMigrationsAsync());

        await db.Database.MigrateAsync(PreDropMigration);

        var mission = (await ReadMissionStateAsync())[missionId];
        Assert.Equal("", mission.Group);
        Assert.Equal("", mission.Purpose);
        Assert.False(await TableExistsAsync("MissionRequestingGroupArchive"));
    }

    private async Task InsertMissionAsync(
        Guid id,
        string number,
        string? group,
        string? purpose,
        bool includeGroup)
    {
        var groupColumn = includeGroup ? "RequestingGroup, " : string.Empty;
        var groupParameter = includeGroup ? "@group, " : string.Empty;
        var parameters = new List<SqlParameter>
        {
            new("@id", id),
            new("@number", number),
            new("@purpose", (object?)purpose ?? DBNull.Value),
        };
        if (includeGroup)
            parameters.Add(new SqlParameter("@group", (object?)group ?? DBNull.Value));

        await ExecuteAsync($"""
            INSERT INTO dbo.Missions
                (Id, MissionNumber, Title, RequesterUsername, RequesterName, RequesterPhoneSnapshot,
                 RequesterOrganizationalUnit, {groupColumn}Type, Purpose, Origin, Destination, RouteJson,
                 PlannedStartAtUtc, PlannedEndAtUtc, Status, UpdatedAtUtc, CreatedAtUtc)
            VALUES
                (@id, @number, N'آزمون', N'migration.test', N'آزمون', N'09120000000',
                 N'واحد آزمون', {groupParameter}0, @purpose, N'مبدأ', N'مقصد', N'[]',
                 SYSUTCDATETIME(), SYSUTCDATETIME(), 0, SYSUTCDATETIME(), SYSUTCDATETIME());
            """, parameters.ToArray());
    }

    private async Task<Dictionary<Guid, string>> ReadArchiveAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MissionId, RequestingGroup FROM dbo.MissionRequestingGroupArchive;";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<Guid, string>();
        while (await reader.ReadAsync())
            result.Add(reader.GetGuid(0), reader.GetString(1));
        return result;
    }

    private async Task<Dictionary<Guid, (string Group, string Purpose)>> ReadMissionStateAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, RequestingGroup, Purpose FROM dbo.Missions;";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<Guid, (string Group, string Purpose)>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetGuid(0),
                (reader.IsDBNull(1) ? "" : reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
        }
        return result;
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var connection = await OpenConnectionAsync();
        return await ScalarAsync<int>(connection,
            "SELECT COUNT(*) FROM sys.tables WHERE name = @name;",
            new SqlParameter("@name", table)) == 1;
    }

    private async Task<bool> ColumnNullableAsync(string column)
    {
        await using var connection = await OpenConnectionAsync();
        return await ScalarAsync<bool>(connection, """
            SELECT c.is_nullable
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            WHERE t.name = N'Missions' AND c.name = @column;
            """, new SqlParameter("@column", column));
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(DbConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync(), typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static string CreateGuardedConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Set {EnvironmentVariable} to a dedicated LocalDB migration test catalog.");

        var builder = new SqlConnectionStringBuilder(value);
        if (!string.Equals(builder.DataSource, DataSource, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Migration SQL tests only allow (localdb)\\MSSQLLocalDB.");
        if (!CatalogPattern.IsMatch(builder.InitialCatalog))
            throw new InvalidOperationException("Migration test catalog must match ^NovaFleet_Migration_Test_[a-f0-9]{32}$.");
        if (!builder.IntegratedSecurity || !string.IsNullOrWhiteSpace(builder.UserID)
            || !string.IsNullOrWhiteSpace(builder.Password) || !string.IsNullOrWhiteSpace(builder.AttachDBFilename))
        {
            throw new InvalidOperationException("Migration SQL tests require integrated Windows authentication without credentials or attached files.");
        }

        // The supplied catalog is only a validated template; every test gets a fresh isolated database.
        builder.InitialCatalog = $"NovaFleet_Migration_Test_{Guid.NewGuid():N}";
        return builder.ConnectionString;
    }

    private async Task RecreateCatalogAsync()
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await ExecuteOnConnectionAsync(connection, $"CREATE DATABASE [{catalog}];");
    }

    private async Task DropCatalogAsync()
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await ExecuteOnConnectionAsync(connection,
            $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}];");
    }

    private static async Task ExecuteOnConnectionAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

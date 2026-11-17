using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Maintenance;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Maintenance;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.IntegrationTests;

public sealed class MaintenanceSqlFactAttribute : FactAttribute
{
    public MaintenanceSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVAFLEET_MAINTENANCE_TEST_SQL")))
            Skip = "Requires the explicitly configured, isolated maintenance SQL test database.";
    }
}

public sealed class MaintenanceSqlServerTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly TestClock clock = new();

    private static NovaFleetDbContext Open()
    {
        var connection = Environment.GetEnvironmentVariable("NOVAFLEET_MAINTENANCE_TEST_SQL")
            ?? throw new InvalidOperationException("An isolated SQL test connection is required.");
        var parsed = new SqlConnectionStringBuilder(connection);
        if (parsed.InitialCatalog != "NovaFleet_Maintenance_Test_20261116" || parsed.DataSource != "localhost")
            throw new InvalidOperationException("Only the approved localhost maintenance test database is allowed.");
        return new NovaFleetDbContext(new DbContextOptionsBuilder<NovaFleetDbContext>().UseSqlServer(connection).Options);
    }

    private MaintenanceService Service(NovaFleetDbContext db) => new(db, new TestUser(), clock);

    private async Task<(Guid Vehicle, Guid Template)> SetupAsync()
    {
        await using var db = Open();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        var vehicle = new Vehicle("آزمون همزمانی", Guid.NewGuid().ToString("N"), IranianLicensePlate.Create(
            Random.Shared.Next(10, 100).ToString(), "ب", Random.Shared.Next(100, 1000).ToString(), Random.Shared.Next(10, 100).ToString()));
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(Ct);
        var template = await Service(db).SaveTemplateAsync(null, new TemplateInput(
            "SQL-" + Guid.NewGuid().ToString("N"), "آزمون", "oil-can", 5000, null, 1000, 30, true, 0), Ct);
        return (vehicle.Id, template.Id);
    }

    private RecordInput Input(Guid template) => new(Guid.NewGuid(), clock.UtcNow.AddDays(-2), 1000,
        "داده ساختگی SQL", null, 50, [new ItemInput(template, null, 100, [new PartInput("فیلتر", null, 2, 300, null)])], 0, null);

    private async Task<RecordDto> SaveAsync(Guid vehicle, Guid? id, RecordInput input)
    {
        await using var db = Open();
        return await Service(db).SaveRecordAsync(vehicle, id, input, Ct);
    }

    [MaintenanceSqlFact]
    public async Task ConcurrentRetriesPersistOneRecordAndOneLedgerEntry()
    {
        var (vehicle, template) = await SetupAsync();
        var input = Input(template);
        var records = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SaveAsync(vehicle, null, input)));
        Assert.Single(records.Select(r => r.Id).Distinct());
        Assert.All(records, r => Assert.Equal(750m, r.TotalCostToman));
        await using var db = Open();
        Assert.Equal(1, await db.MaintenanceRecords.CountAsync(r => r.VehicleId == vehicle, Ct));
        Assert.Equal(1, await db.VehicleOdometerReadings.CountAsync(r => r.VehicleId == vehicle, Ct));
    }

    [MaintenanceSqlFact]
    public async Task ConcurrentCorrectionsAllowOnlyOneVersionAndPreserveOriginal()
    {
        var (vehicle, template) = await SetupAsync();
        var input = Input(template);
        var original = await SaveAsync(vehicle, null, input);
        var outcomes = await Task.WhenAll(Enumerable.Range(1, 2).Select(async extra =>
        {
            try
            {
                await SaveAsync(vehicle, original.Id, input with { Version = 1, OtherCostToman = 50 + extra, Reason = "آزمون ویرایش همزمان" });
                return (Exception?)null;
            }
            catch (ConflictAppException ex) { return ex; }
        }));
        Assert.Equal(1, outcomes.Count(e => e is null));
        Assert.Equal(1, outcomes.Count(e => e is ConflictAppException));
        await using var db = Open();
        var current = await Service(db).RecordAsync(vehicle, original.Id, Ct);
        Assert.Equal(2, current.Version);
        Assert.Single(current.Items);
        Assert.Equal(1, await db.MaintenanceRecordItems.CountAsync(i => i.RecordId == original.Id, Ct));
        var revisions = await Service(db).RevisionsAsync(vehicle, original.Id, Ct);
        Assert.Single(revisions);
        Assert.Contains("750", revisions[0].BeforeJson);
    }

    [MaintenanceSqlFact]
    public async Task RejectedHistoricalReadingLeavesNoRecordOrRevision()
    {
        var (vehicle, template) = await SetupAsync();
        var original = await SaveAsync(vehicle, null, Input(template));
        await Assert.ThrowsAsync<ValidationAppException>(() => SaveAsync(vehicle, null,
            Input(template) with { PerformedAtUtc = clock.UtcNow.AddDays(-1), OdometerKm = 900 }));
        await using var db = Open();
        Assert.Equal(1, await db.MaintenanceRecords.CountAsync(r => r.VehicleId == vehicle, Ct));
        Assert.Equal(1, await db.VehicleOdometerReadings.CountAsync(r => r.VehicleId == vehicle, Ct));
        Assert.Equal(0, await db.MaintenanceRevisions.CountAsync(r => r.VehicleId == vehicle, Ct));
        Assert.Equal(1, (await Service(db).RecordAsync(vehicle, original.Id, Ct)).Version);
    }

    [MaintenanceSqlFact]
    public async Task AttachmentLimitRemainsEnforcedUnderConcurrentUploads()
    {
        var (vehicle, template) = await SetupAsync();
        var record = await SaveAsync(vehicle, null, Input(template));
        var content = "%PDF-1.4\nsynthetic SQL fixture\n%%EOF"u8.ToArray();
        await using (var db = Open())
            for (var i = 0; i < 19; i++)
                await Service(db).AddAttachmentAsync(vehicle, record.Id, $"fixture-{i}.pdf", content, Ct);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            await using var db = Open();
            try
            {
                await Service(db).AddAttachmentAsync(vehicle, record.Id, $"parallel-{i}.pdf", content, Ct);
                return true;
            }
            catch (ValidationAppException) { return false; }
        }));
        Assert.Single(outcomes, success => success);
        await using var check = Open();
        Assert.Equal(20, await check.MaintenanceAttachments.CountAsync(a => a.RecordId == record.Id, Ct));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class TestUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string Username => "maintenance.sql.test";
        public string DisplayName => "آزمون SQL";
        public bool IsLocalAdmin => false;
        public EffectivePermissionSet Permissions { get; } = EffectivePermissionSet.Build(
            [(PermissionCatalog.Maintenance.All, PermissionEffect.Grant)]);
    }
}

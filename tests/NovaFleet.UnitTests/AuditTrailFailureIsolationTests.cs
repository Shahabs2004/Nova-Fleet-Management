using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Auditing;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Auditing;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests;

public sealed class AuditTrailFailureIsolationTests : MissionTestBase
{
    [Fact]
    public async Task WriteAsync_WhenAuditSaveFailsDoesNotPoisonNextSave()
    {
        await using var db = new NovaFleetDbContext(new DbContextOptionsBuilder<NovaFleetDbContext>(Options)
            .AddInterceptors(new FailAuditSaveInterceptor())
            .Options);
        var audit = new AuditTrail(
            db,
            User(RequesterUsername),
            new EmptyRequestContext(),
            NullLogger<AuditTrail>.Instance);

        await db.Missions.AddAsync(NewMission("A001"));
        await db.SaveChangesAsync();
        await audit.WriteAsync(new AuditRecord("AutoStartMission", "Mission", Guid.NewGuid().ToString(), "Success"));

        await db.Missions.AddAsync(NewMission("A002"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Missions.CountAsync());
        Assert.Empty(await db.AuditEntries.ToListAsync());
    }

    private Mission NewMission(string number) =>
        new(number, "آزمون", RequesterUsername, "آزمون", "09120000000", "آزمون",
            MissionType.Freight, null, "مبدأ", "مقصد", null, Hour(0, 10), Hour(0, 11));

    private sealed class FailAuditSaveInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (eventData.Context!.ChangeTracker.Entries<Domain.Auditing.AuditEntry>().Any())
                throw new InvalidOperationException("synthetic audit save failure");

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Domain.Auditing.AuditEntry>().Any())
                throw new InvalidOperationException("synthetic audit save failure");

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

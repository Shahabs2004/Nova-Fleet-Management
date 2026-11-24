using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Infrastructure.Missions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.UnitTests.Missions;

public sealed class MissionResourcePreReadContentionTests : MissionTestBase
{
    [Theory]
    [InlineData("extend")]
    [InlineData("split")]
    public async Task ResourcePreReadConcurrencyFailureIsReturnedAsConflict(string operation)
    {
        var confirmed = await ConfirmAsync();
        if (operation == "split")
        {
            await MarkRunningAsync(confirmed.Id, Hour(0, 10));
            Clock.NowUtc = Hour(0, 12);
        }
        Db.ChangeTracker.Clear();

        var options = new DbContextOptionsBuilder<NovaFleetDbContext>(Options)
            .AddInterceptors(new ThrowOnMissionReadInterceptor())
            .Options;
        await using var db = new NovaFleetDbContext(options);
        var service = new MissionService(db, MissionAdmin(), Audit, Clock, Settings, Notifications, Sms);

        Func<Task> action = operation == "extend"
            ? () => service.ExtendAsync(confirmed.Id, new MissionExtensionRequest(Hour(0, 15), "تمدید آزمون"))
            : () => service.SplitAsync(confirmed.Id,
                new MissionSplitRequest(Hour(0, 12), 1_100, null, null, "تقسیم آزمون", null));

        await Assert.ThrowsAsync<ConflictAppException>(action);
    }

    private sealed class ThrowOnMissionReadInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Missions\"", StringComparison.OrdinalIgnoreCase))
                throw new DbUpdateConcurrencyException("synthetic pre-read concurrency failure");

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaFleet.Application.Common;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Timeline;

namespace NovaFleet.UnitTests;

public sealed class TimelineBoundsTests : Missions.MissionTestBase
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetAsync_RejectsNonPositiveRangeInPersian(int hours)
    {
        var service = new TimelineService(Db, MissionAdmin());
        var exception = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.GetAsync(Hour(0, 10), Hour(0, 10).AddHours(hours), null, null));

        Assert.Equal("بازه زمانی تایم‌لاین معتبر نیست.", exception.Message);
    }

    [Fact]
    public async Task GetAsync_AcceptsExactlyThirtyOneDays()
    {
        var service = new TimelineService(Db, MissionAdmin());
        var rows = await service.GetAsync(Hour(0, 10), Hour(31, 10), null, null);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetAsync_RejectsMoreThanThirtyOneDaysInPersian()
    {
        var service = new TimelineService(Db, MissionAdmin());
        var exception = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.GetAsync(Hour(0, 10), Hour(31, 10).AddTicks(1), null, null));

        Assert.Equal("بازه تایم‌لاین نمی‌تواند بیش از ۳۱ روز باشد.", exception.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsMoreThanTwoThousandMissionsInsteadOfTruncating()
    {
        for (var index = 0; index < 2001; index++)
        {
            Db.Missions.Add(new Mission($"T{index:D4}", "آزمون", RequesterUsername, "آزمون", "09120000000", "آزمون",
                MissionType.Freight, null, "مبدأ", "مقصد", null, Hour(0, 10), Hour(0, 11)));
        }
        await Db.SaveChangesAsync();
        var service = new TimelineService(Db, MissionAdmin());

        var exception = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.GetAsync(Hour(0, 0), Hour(1, 0), null, null));

        Assert.Equal("تعداد مأموریت‌های تایم‌لاین بیش از ۲۰۰۰ است؛ بازه را محدود کنید.", exception.Message);
        Assert.Equal(2001, await Db.Missions.CountAsync());
    }

    [Fact]
    public async Task GetAsync_InsertBetweenCountAndReadCannotBypassResultLimit()
    {
        for (var index = 0; index < 2000; index++)
        {
            Db.Missions.Add(new Mission($"R{index:D4}", "آزمون", RequesterUsername, "آزمون", "09120000000", "آزمون",
                MissionType.Freight, null, "مبدأ", "مقصد", null, Hour(0, 10), Hour(0, 11)));
        }
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var interceptor = new InsertBeforeTimelineReadInterceptor(Db, "R2000");
        var options = new DbContextOptionsBuilder<NovaFleetDbContext>(Options)
            .AddInterceptors(interceptor)
            .Options;
        await using var raceDb = new NovaFleetDbContext(options);
        var service = new TimelineService(raceDb, MissionAdmin());

        var exception = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.GetAsync(Hour(0, 0), Hour(1, 0), null, null));

        Assert.Equal("تعداد مأموریت‌های تایم‌لاین بیش از ۲۰۰۰ است؛ بازه را محدود کنید.", exception.Message);
    }

    private sealed class InsertBeforeTimelineReadInterceptor(NovaFleetDbContext seedDb, string missionNumber)
        : DbCommandInterceptor
    {
        private int missionReads;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Missions\"", StringComparison.OrdinalIgnoreCase)
                && !command.CommandText.Contains("COUNT(", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref missionReads) == 1)
            {
                seedDb.Missions.Add(new Mission(missionNumber, "آزمون", RequesterUsername, "آزمون", "09120000000", "آزمون",
                    MissionType.Freight, null, "مبدأ", "مقصد", null, Hour(0, 10), Hour(0, 11)));
                seedDb.SaveChanges();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

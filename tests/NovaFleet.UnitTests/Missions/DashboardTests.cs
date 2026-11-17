using NovaFleet.Application.Authorization;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Dashboard;

namespace NovaFleet.UnitTests.Missions;

public sealed class DashboardTests : MissionTestBase
{
    [Theory]
    [InlineData("2026-11-17T20:29:59Z", "1405/08/19", "1405/08/26", "1405/09/03")]
    [InlineData("2026-11-17T20:30:00Z", "1405/08/20", "1405/08/27", "1405/09/04")]
    [InlineData("2026-03-20T20:30:00Z", "1404/12/23", "1405/01/01", "1405/01/08")]
    public async Task EmptyWindowUsesTehranTodayAcrossMidnightAndJalaliBoundaries(
        string now, string first, string today, string last)
    {
        Clock.NowUtc = DateTimeOffset.Parse(now);
        var service = new DashboardService(Db,
            User(RequesterUsername, PermissionCatalog.Dashboard.View, PermissionCatalog.Missions.View), Clock);

        var points = (await service.GetAsync()).MissionsByDay;

        Assert.Equal(15, points.Count);
        Assert.Equal(first, points[0].Label);
        Assert.Equal(today, points[7].Label);
        Assert.Equal(last, points[^1].Label);
        Assert.Equal(points[7], Assert.Single(points, point => point.IsToday));
        Assert.Equal(15, points.Select(point => point.Label).Distinct().Count());
        Assert.All(points, point => Assert.Equal(0, point.Value));
    }

    [Fact]
    public async Task CountsPastAndFutureStartsWithinInclusiveSevenDayWindow()
    {
        var today = JalaliDate.StartOfTehranDayUtc(1405, 8, 26);
        Clock.NowUtc = today.AddHours(12);
        var starts = new[]
        {
            today.AddDays(-7).AddHours(-1), today.AddDays(-7),
            today.AddHours(-1), today, today.AddHours(23),
            today.AddDays(1), today.AddDays(7), today.AddDays(7).AddHours(23),
            today.AddDays(8)
        };
        for (var index = 0; index < starts.Length; index++)
        {
            Db.Missions.Add(new Mission($"1405-{index + 1:D4}", "آزمون نمودار", RequesterUsername,
                "درخواست‌کننده", "09123456789", "واحد آزمایش", MissionType.Freight,
                null, "شیراز", "تهران", null, starts[index], starts[index].AddHours(1)));
        }
        await Db.SaveChangesAsync();
        var service = new DashboardService(Db,
            User(RequesterUsername, PermissionCatalog.Dashboard.View, PermissionCatalog.Missions.View), Clock);

        var points = (await service.GetAsync()).MissionsByDay;

        Assert.Equal(new double[] { 1, 0, 0, 0, 0, 0, 1, 2, 1, 0, 0, 0, 0, 0, 2 }, points.Select(point => point.Value));
        Assert.Equal("1405/08/26", Assert.Single(points, point => point.IsToday).Label);
    }

    [Fact]
    public async Task NoMissionPermissionKeepsChartEmpty()
    {
        var service = new DashboardService(Db, User(RequesterUsername, PermissionCatalog.Dashboard.View), Clock);

        Assert.Empty((await service.GetAsync()).MissionsByDay);
    }
}

using NovaFleet.Application.Missions;
using NovaFleet.Domain.Missions;

namespace NovaFleet.UnitTests.Missions;

public sealed class MissionVisibilityTests
{
    [Fact]
    public void Apply_WithoutViewAll_ReturnsOnlyTheRequestersMissions()
    {
        var source = new[] { Mission("own.requester", "خود"), Mission("other.requester", "دیگر") }.AsQueryable();

        var visible = MissionVisibility.Apply(source, false, "own.requester").ToList();

        Assert.Equal("خود", Assert.Single(visible).Title);
        Assert.Equal(2, source.Count());
    }

    [Fact]
    public void Apply_WithViewAll_ReturnsEveryMission()
    {
        var source = new[] { Mission("own.requester", "خود"), Mission("other.requester", "دیگر") }.AsQueryable();

        var visible = MissionVisibility.Apply(source, true, "own.requester").ToList();

        Assert.Equivalent(new[] { "خود", "دیگر" }, visible.Select(item => item.Title));
    }

    private static Mission Mission(string username, string title) =>
        new($"1405-{Guid.NewGuid():N}"[..20], title, username, "درخواست‌کننده", "09123456789", "واحد آزمون",
            MissionType.Freight, null, "شیراز", "تهران", null,
            new DateTimeOffset(2026, 11, 15, 7, 0, 0, TimeSpan.FromMinutes(210)),
            new DateTimeOffset(2026, 11, 15, 11, 0, 0, TimeSpan.FromMinutes(210)));
}

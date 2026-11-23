using NovaFleet.Domain.Missions;

namespace NovaFleet.Application.Missions;

public static class MissionVisibility
{
    public static IQueryable<Mission> Apply(IQueryable<Mission> source, bool canViewAll, string username) =>
        canViewAll ? source : source.Where(item => item.RequesterUsername == username);
}

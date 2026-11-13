using NovaFleet.Domain.Missions;

namespace NovaFleet.UnitTests.Missions;

/// <summary>
/// دودچینی بستر SQLite: ساخت مدل، ساخت جدول‌ها، ثبت درخواست و تأیید با تخصیص منابع.
/// </summary>
public sealed class SqliteFixtureSmokeTests : MissionTestBase
{
    [Fact]
    public async Task CreateThenConfirm_PersistsConfirmedMissionWithResources()
    {
        var mission = await ConfirmAsync();

        Assert.Equal(MissionStatus.Confirmed, mission.Status);
        Assert.NotNull(mission.DriverId);
        Assert.NotNull(mission.VehicleId);
        Assert.Matches(@"^\d{4}-\d{4}$", mission.MissionNumber);

        var fromDb = Reload(mission.Id);
        Assert.Equal(MissionStatus.Confirmed, fromDb.Status);
        Assert.Equal(mission.MissionNumber, fromDb.MissionNumber);
    }
}

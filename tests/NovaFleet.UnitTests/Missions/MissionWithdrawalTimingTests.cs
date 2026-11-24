using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Missions;
using NovaFleet.Domain.Missions;

namespace NovaFleet.UnitTests.Missions;

/// <summary>زمان درخواست انصراف مبنای مهلت بستن خودکار است و رکوردهای قدیمی نیازمند بازبینی دستی‌اند.</summary>
public sealed class MissionWithdrawalTimingTests : MissionTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestWithdrawalAsync_PersistsRequestTimeForConfirmedAndRunningMissions(bool running)
    {
        var confirmed = await ConfirmAsync();
        if (running)
        {
            await MarkRunningAsync(confirmed.Id, Hour(0, 10));
            Clock.NowUtc = Hour(0, 12);
        }

        await ServiceFor(MissionAdmin()).RequestWithdrawalAsync(
            confirmed.Id,
            new MissionWithdrawalRequest("انصراف آزمون"));

        var stored = Reload(confirmed.Id);
        Assert.Equal(Clock.UtcNow, ReadWithdrawalRequestedAt(stored));
        Assert.Equal(MissionStatus.WithdrawalPending, stored.Status);
    }

    [Fact]
    public async Task AutoCloseExpiredWithdrawal_UsesRequestTimeInsteadOfFuturePlannedEnd()
    {
        Clock.NowUtc = Hour(-2, 8);
        var confirmed = await ConfirmAsync(plannedStart: Hour(0, 10), plannedEnd: Hour(0, 14));
        var mission = await Db.Missions.SingleAsync(item => item.Id == confirmed.Id);
        mission.RequestWithdrawal("انصراف قدیمی", Clock.UtcNow);
        await Db.SaveChangesAsync();
        Clock.NowUtc = Hour(0, 9);

        mission.AutoCloseExpiredWithdrawal(Clock.UtcNow);
        await Db.SaveChangesAsync();

        var stored = Reload(confirmed.Id);
        Assert.Equal(MissionStatus.Cancelled, stored.Status);
        Assert.Null(stored.ActualEndAtUtc);
    }

    [Fact]
    public async Task AutoCloseExpiredWithdrawal_WithLegacyNullRequestTimeRequiresManualReview()
    {
        var confirmed = await ConfirmAsync(plannedStart: Hour(0, 10), plannedEnd: Hour(0, 14));
        var mission = await Db.Missions.SingleAsync(item => item.Id == confirmed.Id);
        mission.RequestWithdrawal("رکورد قدیمی", Clock.UtcNow);
        SetWithdrawalRequestedAt(mission, null);
        await Db.SaveChangesAsync();
        Clock.NowUtc = Hour(1, 9);

        var error = Assert.Throws<InvalidOperationException>(() => mission.AutoCloseExpiredWithdrawal(Clock.UtcNow));

        Assert.Contains("دستی", error.Message);
        Assert.Equal(MissionStatus.WithdrawalPending, mission.Status);
        Assert.Null(ReadWithdrawalRequestedAt(mission));
    }

    [Fact]
    public async Task RejectThenNewWithdrawal_StoresTheNewRequestTime()
    {
        var confirmed = await ConfirmAsync();
        var service = ServiceFor(MissionAdmin());
        await service.RequestWithdrawalAsync(confirmed.Id, new MissionWithdrawalRequest("اول"));
        Clock.NowUtc = Hour(0, 10);
        await service.DecideWithdrawalAsync(confirmed.Id, false, new MissionWithdrawalDecision("رد"));
        Clock.NowUtc = Hour(0, 12);

        await service.RequestWithdrawalAsync(confirmed.Id, new MissionWithdrawalRequest("دوباره"));

        Assert.Equal(Hour(0, 12), ReadWithdrawalRequestedAt(Reload(confirmed.Id)));
    }

    private static DateTimeOffset? ReadWithdrawalRequestedAt(Mission mission) =>
        typeof(Mission).GetProperty("WithdrawalRequestedAtUtc")?.GetValue(mission) as DateTimeOffset?;

    private static void SetWithdrawalRequestedAt(Mission mission, DateTimeOffset? value) =>
        typeof(Mission).GetProperty("WithdrawalRequestedAtUtc")?.SetValue(mission, value);
}

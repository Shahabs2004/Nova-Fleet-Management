using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Missions;

namespace NovaFleet.UnitTests.Missions;

/// <summary>مرزهای بازِ نیم‌باز برای تداخل مأموریت‌ها و فاصله حداقلی منابع.</summary>
public sealed class MissionOverlapTests : MissionTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApproveAsync_WithZeroGapAllowsExactEndStartAdjacency(bool sameDriver)
    {
        Settings.Values[SettingKeys.MinimumGapMinutes] = "0";
        var firstResources = SaveReadyResources();
        await ConfirmAsync(firstResources, Hour(0, 10), Hour(0, 14));
        var secondResources = sameDriver ? firstResources : SaveReadyResources();
        var created = await CreatePendingAsync();
        await ServiceFor(MissionAdmin()).UpdatePendingAsync(
            created.Id,
            Request(Hour(0, 14), Hour(0, 16)));

        var approved = await ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id,
            new MissionApprovalRequest(
                sameDriver ? firstResources.Driver.Id : secondResources.Driver.Id,
                sameDriver ? firstResources.Vehicle.Id : secondResources.Vehicle.Id,
                null));

        Assert.Equal(MissionStatus.Confirmed, approved.Status);
    }

    [Fact]
    public async Task ApproveAsync_WithPositiveGapRejectsPaddedAdjacency()
    {
        Settings.Values[SettingKeys.MinimumGapMinutes] = "120";
        var resources = SaveReadyResources();
        await ConfirmAsync(resources, Hour(0, 10), Hour(0, 14));
        var created = await CreatePendingAsync();
        await ServiceFor(MissionAdmin()).UpdatePendingAsync(
            created.Id,
            Request(Hour(0, 15), Hour(0, 17)));

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id,
            new MissionApprovalRequest(resources.Driver.Id, resources.Vehicle.Id, null)));

        Assert.Equal(MissionStatus.Pending, Reload(created.Id).Status);
    }

    [Fact]
    public async Task ApproveAsync_WithPositiveGapAllowsExactlyConfiguredSeparation()
    {
        Settings.Values[SettingKeys.MinimumGapMinutes] = "60";
        var resources = SaveReadyResources();
        await ConfirmAsync(resources, Hour(0, 10), Hour(0, 14));
        var created = await CreatePendingAsync();
        await ServiceFor(MissionAdmin()).UpdatePendingAsync(
            created.Id,
            Request(Hour(0, 16), Hour(0, 18)));

        var approved = await ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id,
            new MissionApprovalRequest(resources.Driver.Id, resources.Vehicle.Id, null));

        Assert.Equal(MissionStatus.Confirmed, approved.Status);
    }

    [Fact]
    public async Task ApproveAsync_WithZeroGapStillRejectsPositiveOverlap()
    {
        Settings.Values[SettingKeys.MinimumGapMinutes] = "0";
        var resources = SaveReadyResources();
        await ConfirmAsync(resources, Hour(0, 10), Hour(0, 14));
        var created = await CreatePendingAsync();
        await ServiceFor(MissionAdmin()).UpdatePendingAsync(
            created.Id,
            Request(Hour(0, 13), Hour(0, 15)));

        await Assert.ThrowsAsync<ConflictAppException>(() => ServiceFor(MissionAdmin()).ApproveAsync(
            created.Id,
            new MissionApprovalRequest(resources.Driver.Id, resources.Vehicle.Id, null)));
    }
}

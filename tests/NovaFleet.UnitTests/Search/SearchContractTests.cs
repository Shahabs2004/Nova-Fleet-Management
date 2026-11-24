using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Locations;
using NovaFleet.Application.Notifications;
using NovaFleet.Domain.Locations;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Locations;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Search;

/// <summary>پارامتر search باید در سرویس‌های پیامک و مکان همان فیلتر مورد انتظار endpoint را اعمال کند.</summary>
public sealed class SearchContractTests : MissionTestBase
{
    private static ICurrentUser SearchUser() => User(RequesterUsername,
        PermissionCatalog.Missions.View, PermissionCatalog.Notifications.SmsView);

    [Fact]
    public async Task SmsList_SearchFiltersRecipientMobileAndName()
    {
        Db.SmsQueueItems.AddRange(
            new SmsQueueItem(SmsEventType.MissionApproved, "09121112233", "گیرنده ویژه", "آزمون", nowUtc: Clock.UtcNow),
            new SmsQueueItem(SmsEventType.MissionApproved, "09124445566", "گیرنده عادی", "آزمون", nowUtc: Clock.UtcNow));
        await Db.SaveChangesAsync();
        var service = new SmsService(Db, Settings, SearchUser(), Audit, Clock);

        var byName = await service.ListAsync(new SmsQuery("ویژه", null, null, null, 1, 20));
        var byMobile = await service.ListAsync(new SmsQuery("09121112233", null, null, null, 1, 20));

        Assert.Equal(1, byName.TotalCount);
        Assert.Equal("گیرنده ویژه", Assert.Single(byName.Items).RecipientName);
        Assert.Equal(1, byMobile.TotalCount);
        Assert.Equal("09121112233", Assert.Single(byMobile.Items).RecipientMobile);
    }

    [Fact]
    public async Task LocationList_SearchFiltersName()
    {
        Db.Locations.AddRange(new Location("انبار مرکزی", RequesterUsername), new Location("پارکینگ", RequesterUsername));
        await Db.SaveChangesAsync();
        var service = new LocationService(Db, SearchUser(), Audit);

        var result = await service.ListAsync(null, "انبار");

        Assert.Equal("انبار مرکزی", Assert.Single(result).Name);
    }
}

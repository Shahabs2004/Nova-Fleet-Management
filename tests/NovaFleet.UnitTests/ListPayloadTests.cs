using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Drivers;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Drivers;
using NovaFleet.Infrastructure.Drivers;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.UnitTests.Missions;
using Xunit;

namespace NovaFleet.UnitTests;

public sealed class ListPayloadTests : MissionTestBase
{
    private const string Avatar = "data:image/png;base64,YQ==";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Driver_list_and_options_omit_avatar_but_detail_retains_it()
    {
        var driver = NewDriver();
        driver.SetAvatar(Avatar);
        await Db.SaveChangesAsync();
        var service = new DriverService(Db, User(RequesterUsername,
            PermissionCatalog.Drivers.View, PermissionCatalog.Drivers.Manage), Audit);

        var list = await service.SearchAsync(null, false, 1, 20);
        var options = await service.ListOptionsAsync(false);
        var detail = await service.GetAsync(driver.Id);

        Assert.DoesNotContain("avatarDataUrl", JsonSerializer.Serialize(list, JsonOptions), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("avatarDataUrl", JsonSerializer.Serialize(options, JsonOptions), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Avatar, detail.AvatarDataUrl);
        Assert.Contains("avatarDataUrl", JsonSerializer.Serialize(detail, JsonOptions), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task User_list_omits_avatar_but_detail_and_profile_retain_it()
    {
        var user = await Db.Users.SingleAsync(item => item.Username == RequesterUsername);
        user.SetAvatar(Avatar);
        await Db.SaveChangesAsync();
        var service = new UserService(Db, new NoopPasswordHasher(), User(RequesterUsername,
            PermissionCatalog.Administration.UsersManage), Audit);

        var list = await service.ListAsync(null, false);
        var detail = await service.GetAsync(user.Id);
        var profile = await service.GetProfileAsync();

        Assert.DoesNotContain("avatarDataUrl", JsonSerializer.Serialize(list, JsonOptions), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Avatar, detail.AvatarDataUrl);
        Assert.Equal(Avatar, profile.AvatarDataUrl);
    }

    private sealed class NoopPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string passwordHash, string password) => passwordHash == password;
    }
}

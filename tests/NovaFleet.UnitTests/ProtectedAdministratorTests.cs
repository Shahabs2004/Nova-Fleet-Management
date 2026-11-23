using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Authorization;

public sealed class ProtectedAdministratorTests : MissionTestBase
{
    private const string ManagerUsername = "demo.manager";
    private const string AdminUsername = "demo.localadmin";

    private UserService UsersFor(ICurrentUser user) => new(Db, new StubPasswordHasher(), user, Audit);

    private ApplicationUser SeedManager(bool localAdmin = false, bool disabled = false)
    {
        var user = new ApplicationUser(ManagerUsername, "مدیر", "کاربران", localAdmin && !disabled);
        if (disabled)
        {
            user.Disable();
            user.MarkLocalAdmin(true);
        }

        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    private Role SeedRole(string name = "SystemAdministrator")
    {
        var role = new Role(name, "مدیر سامانه", null, isSystem: true);
        Db.Roles.Add(role);
        Db.SaveChanges();
        return role;
    }

    private ApplicationUser SeedTarget(bool localAdmin = false)
    {
        var user = new ApplicationUser("target.user", "هدف", "آزمون", localAdmin);
        user.SetPasswordHash(new StubPasswordHasher().Hash("Old#Password1"));
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    private static FakeCurrentUser Manager(bool claimsLocalAdmin = false) =>
        new(ManagerUsername, PermissionCatalog.Administration.UsersManage) { IsLocalAdmin = claimsLocalAdmin };

    private static CreateLocalUserRequest CreateRequest(bool isLocalAdmin, IReadOnlyList<Guid>? roleIds = null) =>
        new("new.local", "New#Password1", "کاربر", "جدید", null, null, null, isLocalAdmin, roleIds ?? [], null);

    private static UpdateUserRequest UpdateRequest(IReadOnlyList<Guid> roleIds) =>
        new("هدف", "آزمون", null, null, null, roleIds, null);

    [Fact]
    public async Task CreateLocalAdmin_RejectsOrdinaryUserManagerWithoutWriting()
    {
        SeedManager();

        await Assert.ThrowsAsync<ForbiddenAppException>(() => UsersFor(Manager()).CreateLocalAsync(CreateRequest(true)));

        Assert.False(Db.Users.AsNoTracking().Any(item => item.Username == "new.local"));
    }

    [Fact]
    public async Task CreateLocalAdmin_RejectsStaleOrDisabledLocalAdminClaim()
    {
        SeedManager(localAdmin: true, disabled: true);

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => UsersFor(Manager(claimsLocalAdmin: true)).CreateLocalAsync(CreateRequest(true)));

        Assert.False(Db.Users.AsNoTracking().Any(item => item.Username == "new.local"));
    }

    [Fact]
    public async Task CreateLocalAdmin_AllowsActiveDatabaseLocalAdmin()
    {
        SeedManager(localAdmin: true);

        var created = await UsersFor(Manager(claimsLocalAdmin: true)).CreateLocalAsync(CreateRequest(true));

        Assert.True(created.IsLocalAdmin);
        Assert.True(Db.Users.AsNoTracking().Single(item => item.Id == created.Id).IsLocalAdmin);
    }

    [Fact]
    public async Task ProtectedRoleAssignment_RejectsOrdinaryUserManagerWithoutChangingRoles()
    {
        SeedManager();
        var role = SeedRole();
        var target = SeedTarget();

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => UsersFor(Manager()).UpdateAsync(target.Id, UpdateRequest([role.Id])));

        Assert.False(Db.UserRoles.AsNoTracking().Any(item => item.UserId == target.Id && item.RoleId == role.Id));
    }

    [Fact]
    public async Task ProtectedRoleRemoval_RejectsOrdinaryUserManagerWithoutChangingRoles()
    {
        SeedManager();
        var role = SeedRole("systemadministrator");
        var target = SeedTarget();
        Db.UserRoles.Add(new UserRole(target.Id, role.Id));
        Db.SaveChanges();

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => UsersFor(Manager()).UpdateAsync(target.Id, UpdateRequest([])));

        Assert.True(Db.UserRoles.AsNoTracking().Any(item => item.UserId == target.Id && item.RoleId == role.Id));
    }

    [Fact]
    public async Task ProtectedRoleChanges_AllowActiveDatabaseLocalAdmin()
    {
        SeedManager(localAdmin: true);
        var role = SeedRole();
        var target = SeedTarget();
        var service = UsersFor(Manager(claimsLocalAdmin: true));

        await service.UpdateAsync(target.Id, UpdateRequest([role.Id]));
        Assert.True(Db.UserRoles.AsNoTracking().Any(item => item.UserId == target.Id && item.RoleId == role.Id));

        await service.UpdateAsync(target.Id, UpdateRequest([]));
        Assert.False(Db.UserRoles.AsNoTracking().Any(item => item.UserId == target.Id && item.RoleId == role.Id));
    }

    [Fact]
    public async Task LocalAdminPasswordReset_RejectsOrdinaryUserManagerWithoutChangingPassword()
    {
        SeedManager();
        var target = SeedTarget(localAdmin: true);
        var originalHash = target.PasswordHash;

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => UsersFor(Manager()).ResetPasswordAsync(target.Id, new ResetPasswordRequest("New#Password1")));

        Db.ChangeTracker.Clear();
        Assert.Equal(originalHash, Db.Users.AsNoTracking().Single(item => item.Id == target.Id).PasswordHash);
    }

    [Fact]
    public async Task ProtectedRolePasswordReset_RejectsOrdinaryUserManagerWithoutChangingPassword()
    {
        SeedManager();
        var role = SeedRole("systemadministrator");
        var target = SeedTarget();
        Db.UserRoles.Add(new UserRole(target.Id, role.Id));
        Db.SaveChanges();
        var originalHash = target.PasswordHash;

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => UsersFor(Manager()).ResetPasswordAsync(target.Id, new ResetPasswordRequest("New#Password1")));

        Db.ChangeTracker.Clear();
        Assert.Equal(originalHash, Db.Users.AsNoTracking().Single(item => item.Id == target.Id).PasswordHash);
    }

    [Fact]
    public async Task LocalAdminPasswordReset_AllowsActiveDatabaseLocalAdmin()
    {
        SeedManager(localAdmin: true);
        var target = SeedTarget(localAdmin: true);
        var hasher = new StubPasswordHasher();

        await UsersFor(Manager(claimsLocalAdmin: true)).ResetPasswordAsync(target.Id, new ResetPasswordRequest("New#Password1"));

        Db.ChangeTracker.Clear();
        Assert.True(hasher.Verify(Db.Users.AsNoTracking().Single(item => item.Id == target.Id).PasswordHash!, "New#Password1"));
    }

    [Fact]
    public async Task LocalAdminDisable_RejectsOrdinaryUserManagerAndPreservesLastAdmin()
    {
        SeedManager();
        var target = SeedTarget(localAdmin: true);

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => UsersFor(Manager()).SetDisabledAsync(target.Id, true));

        Assert.False(Db.Users.AsNoTracking().Single(item => item.Id == target.Id).IsDisabled);
    }

    [Fact]
    public async Task LocalAdminDisable_RemainsBlockedForActiveLocalAdmin()
    {
        SeedManager(localAdmin: true);
        var target = SeedTarget(localAdmin: true);

        await Assert.ThrowsAsync<ValidationAppException>(
            () => UsersFor(Manager(claimsLocalAdmin: true)).SetDisabledAsync(target.Id, true));

        Assert.False(Db.Users.AsNoTracking().Single(item => item.Id == target.Id).IsDisabled);
    }

    [Fact]
    public void ApplicationUser_Disable_AlwaysRejectsLocalAdministrator()
    {
        var admin = SeedTarget(localAdmin: true);

        Assert.Throws<InvalidOperationException>(() => admin.Disable());
        Assert.False(admin.IsDisabled);
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string password) => passwordHash == $"hashed:{password}";
    }
}

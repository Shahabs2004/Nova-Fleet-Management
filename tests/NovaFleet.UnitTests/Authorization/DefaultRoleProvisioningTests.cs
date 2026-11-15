using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Security;
using NovaFleet.UnitTests.Missions;
using System.Security.Cryptography;
using System.Text;

namespace NovaFleet.UnitTests.Authorization;

/// <summary>نقش پیش‌فرض کاربران جدید دایرکتوری: تخصیص در نخستین ورود و مدیریت تنظیم آن.</summary>
public class DefaultRoleProvisioningTests : MissionTestBase
{
    private const string NewUsername = "new.directory";
    private const string ExistingUsername = "existing.directory";

    private Role SeedRole(string name, string title, params string[] permissions)
    {
        var role = new Role(name, title, null, isSystem: true);
        Db.Roles.Add(role);
        Db.RolePermissions.AddRange(permissions.Select(code => new RolePermission(role.Id, code, PermissionEffect.Grant)));
        Db.SaveChanges();
        return role;
    }

    private RoleService RolesFor(ICurrentUser user) => new(Db, user, Audit, Runtime);

    private static FakeCurrentUser AuthorizationManager() =>
        new("demo.authz", PermissionCatalog.Administration.AuthorizationManage);

    private AuthService AuthFor(FakeDirectory directory) =>
        new(Db, new StubPasswordHasher(), directory, new PermissionResolver(Db), Clock);

    private static FakeDirectory DirectoryFor(string username, params string[] groups) =>
        new() { User = new DirectoryUserInfo(username, "کاربر جدید", null, null, groups, null) };

    private static SessionContext Session() => new(Guid.NewGuid().ToString(), "127.0.0.1");

    [Fact]
    public async Task Ldap_NewUser_GetsDefaultRequesterRole()
    {
        SeedRole("Requester", "درخواست‌کننده", PermissionCatalog.Missions.Create);
        var auth = AuthFor(DirectoryFor(NewUsername));

        var current = await auth.LoginLdapAsync(new LoginRequest(NewUsername, "pass"), Session());

        Assert.Contains("Requester", current.Roles);
        Assert.Contains(PermissionCatalog.Missions.Create, current.Permissions);
        var user = Db.Users.Single(item => item.Username == NewUsername);
        Assert.True(Db.UserRoles.Any(item => item.UserId == user.Id));
    }

    [Fact]
    public async Task Ldap_NewUser_RespectsConfiguredDefaultRole()
    {
        SeedRole("Requester", "درخواست‌کننده", PermissionCatalog.Missions.Create);
        SeedRole("Viewer", "ناظر", PermissionCatalog.Dashboard.View);
        await Runtime.SetAsync(SettingKeys.DefaultUserRole, "Viewer");
        var auth = AuthFor(DirectoryFor(NewUsername));

        var current = await auth.LoginLdapAsync(new LoginRequest(NewUsername, "pass"), Session());

        Assert.Contains("Viewer", current.Roles);
        Assert.DoesNotContain("Requester", current.Roles);
    }

    [Fact]
    public async Task Ldap_NewUser_EmptyDefaultRoleStaysForbidden()
    {
        SeedRole("Requester", "درخواست‌کننده", PermissionCatalog.Missions.Create);
        await Runtime.SetAsync(SettingKeys.DefaultUserRole, string.Empty);
        var auth = AuthFor(DirectoryFor(NewUsername));

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => auth.LoginLdapAsync(new LoginRequest(NewUsername, "pass"), Session()));

        var user = Db.Users.Single(item => item.Username == NewUsername);
        Assert.False(Db.UserRoles.Any(item => item.UserId == user.Id));
    }

    [Fact]
    public async Task Ldap_ExistingUser_IsNotGivenDefaultRole()
    {
        SeedRole("Requester", "درخواست‌کننده", PermissionCatalog.Missions.Create);
        Db.Users.Add(new ApplicationUser(ExistingUsername, "قدیمی", "کاربر", isDirectoryUser: true));
        Db.SaveChanges();
        var auth = AuthFor(DirectoryFor(ExistingUsername));

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => auth.LoginLdapAsync(new LoginRequest(ExistingUsername, "pass"), Session()));

        var user = Db.Users.Single(item => item.Username == ExistingUsername);
        Assert.False(Db.UserRoles.Any(item => item.UserId == user.Id));
    }

    [Fact]
    public async Task Windows_NewUser_GetsDefaultRequesterRole()
    {
        SeedRole("Requester", "درخواست‌کننده", PermissionCatalog.Missions.Create);
        var auth = AuthFor(DirectoryFor(NewUsername));

        var current = await auth.LoginWindowsAsync($"Frs\\{NewUsername}", Session());

        Assert.Contains("Requester", current.Roles);
        var user = Db.Users.Single(item => item.Username == NewUsername);
        Assert.True(Db.UserRoles.Any(item => item.UserId == user.Id));
    }

    [Fact]
    public async Task Ldap_NewUser_DefaultRoleStacksWithAdGroupMapping()
    {
        SeedRole("Requester", "درخواست‌کننده", PermissionCatalog.Missions.Create);
        var manager = SeedRole("FleetManager", "مدیر ناوگان", PermissionCatalog.Missions.Approve);
        Db.DirectoryGroupRoleMappings.Add(new DirectoryGroupRoleMapping("DCManager", manager.Id));
        Db.SaveChanges();
        var auth = AuthFor(DirectoryFor(NewUsername, "DCManager"));

        var current = await auth.LoginLdapAsync(new LoginRequest(NewUsername, "pass"), Session());

        Assert.Contains("Requester", current.Roles);
        Assert.Contains("FleetManager", current.Roles);
        Assert.Contains(PermissionCatalog.Missions.Approve, current.Permissions);
    }

    [Fact]
    public async Task RoleService_GetDefaultRole_FallsBackToRequester()
    {
        var dto = await RolesFor(AuthorizationManager()).GetDefaultRoleAsync();

        Assert.Equal("Requester", dto.RoleName);
    }

    [Fact]
    public async Task RoleService_SetDefaultRole_PersistsAndAudits()
    {
        SeedRole("Viewer", "ناظر", PermissionCatalog.Dashboard.View);
        var service = RolesFor(AuthorizationManager());

        var dto = await service.SetDefaultRoleAsync(new SetDefaultRoleRequest("Viewer"));

        Assert.Equal("Viewer", dto.RoleName);
        Assert.Equal("Viewer", await Runtime.GetStringAsync(SettingKeys.DefaultUserRole));
        Assert.True(Audit.Wrote("SetDefaultUserRole"));
    }

    [Fact]
    public async Task RoleService_SetDefaultRole_RejectsUnknownRole()
    {
        await Assert.ThrowsAsync<ValidationAppException>(
            () => RolesFor(AuthorizationManager()).SetDefaultRoleAsync(new SetDefaultRoleRequest("Ghost")));
    }

    [Fact]
    public async Task RoleService_SetDefaultRole_AllowsEmptyToDisable()
    {
        var service = RolesFor(AuthorizationManager());

        var dto = await service.SetDefaultRoleAsync(new SetDefaultRoleRequest("  "));

        Assert.Equal(string.Empty, dto.RoleName);
        Assert.Equal(string.Empty, await Runtime.GetStringAsync(SettingKeys.DefaultUserRole));
    }

    [Fact]
    public async Task RoleService_DefaultRole_NeedsAuthorizationManage()
    {
        var service = RolesFor(new FakeCurrentUser("demo.operator", PermissionCatalog.Missions.View));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetDefaultRoleAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => service.SetDefaultRoleAsync(new SetDefaultRoleRequest("Requester")));
    }

    private sealed class FakeDirectory : IDirectoryAuthenticator
    {
        public DirectoryUserInfo? User { get; set; }

        public Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
            => Task.FromResult(User is null ? DirectoryAuthResult.Failed("bad") : DirectoryAuthResult.Succeeded(User));

        public Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult(User);

        public Task<DirectoryProbeResult> ProbeAsync(string? sampleUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryProbeResult.Bound(1, false));
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

        public bool Verify(string passwordHash, string password) => Hash(password) == passwordHash;
    }
}
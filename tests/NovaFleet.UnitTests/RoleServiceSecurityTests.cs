using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Setup;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Authorization;

public sealed class RoleServiceSecurityTests : MissionTestBase
{
    private const string ManagerUsername = "role.manager";

    [Fact]
    public async Task DefaultRole_OrdinaryAuthorizationManagerCannotSelectSystemAdministrator()
    {
        var systemRole = SeedSystemAdministrator();
        var service = Service(ManagerUsername, isLocalAdmin: false);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            service.SetDefaultRoleAsync(new SetDefaultRoleRequest(systemRole.Name)));

        Assert.Equal(SettingKeys.DefaultUserRoleValue,
            (await service.GetDefaultRoleAsync()).RoleName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Viewer")]
    public async Task DefaultRole_OrdinaryAuthorizationManagerCannotReplaceProtectedCurrentRole(string nextRole)
    {
        SeedSystemAdministrator();
        SeedRole("Viewer", "ناظر");
        await Runtime.SetAsync(SettingKeys.DefaultUserRole, "SystemAdministrator");
        var service = Service(ManagerUsername, isLocalAdmin: false);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            service.SetDefaultRoleAsync(new SetDefaultRoleRequest(nextRole)));

        Assert.Equal("SystemAdministrator",
            (await service.GetDefaultRoleAsync()).RoleName);
    }

    [Fact]
    public async Task DefaultRole_ActiveLocalAdministratorCanReplaceProtectedCurrentRole()
    {
        SeedSystemAdministrator();
        SeedRole("Viewer", "ناظر");
        SeedLocalAdministrator();
        await Runtime.SetAsync(SettingKeys.DefaultUserRole, "SystemAdministrator");
        var service = Service(ManagerUsername, isLocalAdmin: true);

        var result = await service.SetDefaultRoleAsync(new SetDefaultRoleRequest("Viewer"));

        Assert.Equal("Viewer", result.RoleName);
    }

    [Fact]
    public async Task DefaultRole_ActiveLocalAdministratorCanSelectSystemAdministrator()
    {
        var systemRole = SeedSystemAdministrator();
        SeedLocalAdministrator();
        var service = Service(ManagerUsername, isLocalAdmin: true);

        var result = await service.SetDefaultRoleAsync(new SetDefaultRoleRequest(systemRole.Name));

        Assert.Equal(systemRole.Name, result.RoleName);
    }

    [Fact]
    public async Task DirectoryMapping_OrdinaryAuthorizationManagerCannotAssignSystemAdministrator()
    {
        var systemRole = SeedSystemAdministrator();
        var service = Service(ManagerUsername, isLocalAdmin: false);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            service.SaveDirectoryMappingAsync(new SaveDirectoryMappingRequest("NovaFleet-Test", systemRole.Id)));

        Assert.Empty(await Db.DirectoryGroupRoleMappings.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DirectoryMapping_OrdinaryAuthorizationManagerCannotReplaceProtectedCurrentRole()
    {
        var systemRole = SeedSystemAdministrator();
        var viewer = SeedRole("Viewer", "ناظر");
        var mapping = new DirectoryGroupRoleMapping("NovaFleet-Test", systemRole.Id);
        Db.DirectoryGroupRoleMappings.Add(mapping);
        Db.SaveChanges();
        var service = Service(ManagerUsername, isLocalAdmin: false);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            service.SaveDirectoryMappingAsync(new SaveDirectoryMappingRequest("NovaFleet-Test", viewer.Id)));

        Assert.Equal(systemRole.Id,
            Db.DirectoryGroupRoleMappings.AsNoTracking().Single().RoleId);
    }

    [Fact]
    public async Task DirectoryMapping_OrdinaryAuthorizationManagerCannotRemoveProtectedCurrentRole()
    {
        var systemRole = SeedSystemAdministrator();
        var mapping = new DirectoryGroupRoleMapping("NovaFleet-Test", systemRole.Id);
        Db.DirectoryGroupRoleMappings.Add(mapping);
        Db.SaveChanges();
        var service = Service(ManagerUsername, isLocalAdmin: false);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            service.RemoveDirectoryMappingAsync(mapping.Id));

        Assert.Single(Db.DirectoryGroupRoleMappings.AsNoTracking());
    }

    [Fact]
    public async Task DirectoryMapping_ActiveLocalAdministratorCanReplaceAndRemoveProtectedCurrentRole()
    {
        var systemRole = SeedSystemAdministrator();
        var viewer = SeedRole("Viewer", "ناظر");
        var mapping = new DirectoryGroupRoleMapping("NovaFleet-Test", systemRole.Id);
        Db.DirectoryGroupRoleMappings.Add(mapping);
        Db.SaveChanges();
        SeedLocalAdministrator();
        var service = Service(ManagerUsername, isLocalAdmin: true);

        var replaced = await service.SaveDirectoryMappingAsync(
            new SaveDirectoryMappingRequest("NovaFleet-Test", viewer.Id));
        Assert.Equal(viewer.Id, replaced.RoleId);

        await service.RemoveDirectoryMappingAsync(replaced.Id);
        Assert.Empty(Db.DirectoryGroupRoleMappings.AsNoTracking());
    }

    [Fact]
    public async Task DirectoryMapping_ActiveLocalAdministratorCanAssignSystemAdministrator()
    {
        var systemRole = SeedSystemAdministrator();
        SeedLocalAdministrator();
        var service = Service(ManagerUsername, isLocalAdmin: true);

        var result = await service.SaveDirectoryMappingAsync(new SaveDirectoryMappingRequest("NovaFleet-Test", systemRole.Id));

        Assert.Equal(systemRole.Id, result.RoleId);
    }

    [Fact]
    public async Task SystemRolePermissions_OrdinaryAuthorizationManagerCannotChangeWildcard()
    {
        var systemRole = SeedSystemAdministrator();
        var service = Service(ManagerUsername, isLocalAdmin: false);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            service.SetPermissionsAsync(systemRole.Id, new SetPermissionsRequest([])));

        var permission = await Db.RolePermissions.AsNoTracking().SingleAsync();
        Assert.Equal(PermissionCatalog.All, permission.Permission);
    }

    [Fact]
    public async Task SystemRolePermissions_StaleLocalAdministratorClaimIsRejected()
    {
        var systemRole = SeedSystemAdministrator();
        var service = Service(ManagerUsername, isLocalAdmin: true);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            service.SetPermissionsAsync(systemRole.Id, new SetPermissionsRequest([])));

        var permission = await Db.RolePermissions.AsNoTracking().SingleAsync();
        Assert.Equal(PermissionCatalog.All, permission.Permission);
    }

    [Fact]
    public async Task FleetManagerDefault_IncludesSessionViewAndManagementPermissions()
    {
        await DatabaseInitializer.SeedAsync(Db);

        var role = await Db.Roles.AsNoTracking().SingleAsync(item => item.Name == "FleetManager");
        Assert.True(await Db.RolePermissions.AnyAsync(item =>
            item.RoleId == role.Id && item.Permission == PermissionCatalog.Security.SessionsView));
        Assert.True(await Db.RolePermissions.AnyAsync(item =>
            item.RoleId == role.Id && item.Permission == PermissionCatalog.Security.SessionsManage));
    }

    private Role SeedSystemAdministrator()
    {
        var role = SeedRole("SystemAdministrator", "مدیر سامانه");
        Db.RolePermissions.Add(new RolePermission(role.Id, PermissionCatalog.All, PermissionEffect.Grant));
        Db.SaveChanges();
        return role;
    }

    private Role SeedRole(string name, string title)
    {
        var role = new Role(name, title, null, isSystem: true);
        Db.Roles.Add(role);
        Db.SaveChanges();
        return role;
    }

    private void SeedLocalAdministrator()
    {
        Db.Users.Add(new ApplicationUser(ManagerUsername, "مدیر", "محلی", isLocalAdmin: true));
        Db.SaveChanges();
    }

    private RoleService Service(string username, bool isLocalAdmin)
    {
        var user = User(username, PermissionCatalog.Administration.AuthorizationManage);
        return new RoleService(Db,
            new FakeCurrentUser(user.Username, user.DisplayName, user.Permissions) { IsLocalAdmin = isLocalAdmin },
            Audit, Runtime);
    }
}

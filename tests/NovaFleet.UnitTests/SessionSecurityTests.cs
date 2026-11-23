using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Application.Sessions;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Sessions;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Security;
using NovaFleet.Infrastructure.Sessions;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Authorization;

public sealed class SessionSecurityTests : MissionTestBase
{
    private const string SessionsManage = "security.sessions.manage";

    [Fact]
    public async Task ForceEnd_RejectsViewOnlyPermissionAndLeavesSessionActive()
    {
        var session = SeedSession("demo.other");
        var service = new SessionService(Db, User("demo.viewer", PermissionCatalog.Security.SessionsView), Clock);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.ForceEndAsync(session.Id));

        Assert.Equal(UserSessionStatus.Active, Db.UserSessions.AsNoTracking().Single(item => item.Id == session.Id).Status);
    }

    [Fact]
    public async Task ForceEnd_AllowsDedicatedManagePermission()
    {
        var session = SeedSession("demo.other");
        var service = new SessionService(Db, User("demo.manager", SessionsManage), Clock);

        await service.ForceEndAsync(session.Id);

        var ended = Db.UserSessions.AsNoTracking().Single(item => item.Id == session.Id);
        Assert.Equal(UserSessionStatus.Ended, ended.Status);
        Assert.Equal(Clock.UtcNow, ended.EndedAtUtc);
    }

    [Fact]
    public async Task SessionsList_StillAllowsViewOnlyPermission()
    {
        SeedSession("demo.other");
        var service = new SessionService(Db, User("demo.viewer", PermissionCatalog.Security.SessionsView), Clock);

        var result = await service.ListAsync(new SessionQuery(null, null, null, null, 1, 20));

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task PasswordReset_DeletesOnlyTargetUsersSessions()
    {
        var caller = SeedLocalAdmin();
        var target = SeedLocalUser("target.user");
        SeedSession(target.Username);
        SeedSession(target.Username);
        var otherSession = SeedSession("other.user");
        var service = new UserService(Db, new StubPasswordHasher(), Caller(caller), Audit);

        await service.ResetPasswordAsync(target.Id, new ResetPasswordRequest("New#Password1"));

        Assert.False(Db.UserSessions.AsNoTracking().Any(item => item.Username == target.Username));
        Assert.True(Db.UserSessions.AsNoTracking().Any(item => item.Id == otherSession.Id));
    }

    [Fact]
    public async Task PasswordChange_DeletesAllSessionsBeforeSuccess()
    {
        var user = SeedLocalUser("password.user");
        SeedSession(user.Username);
        SeedSession(user.Username);
        var auth = Auth();

        await auth.ChangePasswordAsync(user.Username, "Old#Password1", "New#Password1");

        Assert.False(Db.UserSessions.AsNoTracking().Any(item => item.Username == user.Username));
        Db.ChangeTracker.Clear();
        var changed = Db.Users.AsNoTracking().Single(item => item.Id == user.Id);
        Assert.True(new StubPasswordHasher().Verify(changed.PasswordHash!, "New#Password1"));
    }

    [Fact]
    public async Task IncorrectCurrentPassword_DoesNotRevokeSessions()
    {
        var user = SeedLocalUser("password.user");
        var session = SeedSession(user.Username);

        await Assert.ThrowsAsync<UnauthorizedAppException>(
            () => Auth().ChangePasswordAsync(user.Username, "wrong", "New#Password1"));

        Assert.Equal(UserSessionStatus.Active, Db.UserSessions.AsNoTracking().Single(item => item.Id == session.Id).Status);
    }

    [Fact]
    public async Task PasswordChange_UserUpdateFailureRollsBackSessionDeletion()
    {
        var user = SeedLocalUser("password.user");
        var originalHash = user.PasswordHash;
        SeedSession(user.Username);
        SeedSession(user.Username);
        await FailUserUpdatesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Auth().ChangePasswordAsync(user.Username, "Old#Password1", "New#Password1"));

        Db.ChangeTracker.Clear();
        Assert.Equal(originalHash, Db.Users.AsNoTracking().Single(item => item.Id == user.Id).PasswordHash);
        Assert.Equal(2, Db.UserSessions.AsNoTracking().Count(item => item.Username == user.Username));
    }

    [Fact]
    public async Task PasswordReset_UserUpdateFailureRollsBackSessionDeletion()
    {
        var caller = SeedLocalAdmin();
        var target = SeedLocalUser("target.user");
        var originalHash = target.PasswordHash;
        SeedSession(target.Username);
        SeedSession(target.Username);
        await FailUserUpdatesAsync();
        var service = new UserService(Db, new StubPasswordHasher(), Caller(caller), Audit);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.ResetPasswordAsync(target.Id, new ResetPasswordRequest("New#Password1")));

        Db.ChangeTracker.Clear();
        Assert.Equal(originalHash, Db.Users.AsNoTracking().Single(item => item.Id == target.Id).PasswordHash);
        Assert.Equal(2, Db.UserSessions.AsNoTracking().Count(item => item.Username == target.Username));
    }

    private async Task FailUserUpdatesAsync()
    {
        await using var command = Db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            CREATE TRIGGER FailUserUpdate
            BEFORE UPDATE ON Users
            BEGIN
                SELECT RAISE(ABORT, 'forced user update failure');
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private UserSession SeedSession(string username)
    {
        var session = Db.UserSessions.Add(new UserSession(username, UserSessionLoginMethod.Local, "127.0.0.1")).Entity;
        Db.SaveChanges();
        return session;
    }

    private ApplicationUser SeedLocalUser(string username)
    {
        var user = new ApplicationUser(username, "کاربر", "آزمون");
        user.SetPasswordHash(new StubPasswordHasher().Hash("Old#Password1"));
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    private ApplicationUser SeedLocalAdmin()
    {
        var user = new ApplicationUser("local.admin", "مدیر", "محلی", isLocalAdmin: true);
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    private static FakeCurrentUser Caller(ApplicationUser user) =>
        new(user.Username, PermissionCatalog.Administration.UsersManage) { IsLocalAdmin = true };

    private AuthService Auth() => new(
        Db,
        new StubPasswordHasher(),
        new StubDirectory(),
        new PermissionResolver(Db),
        Clock);

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string password) => passwordHash == $"hashed:{password}";
    }

    private sealed class StubDirectory : IDirectoryAuthenticator
    {
        public Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryAuthResult.Failed("unused"));

        public Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult<DirectoryUserInfo?>(null);

        public Task<DirectoryProbeResult> ProbeAsync(string? sampleUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryProbeResult.Bound(0, false));

        public Task<DirectoryGroupsResult> ListGroupsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryGroupsResult.Loaded([], false));
    }
}

using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Sessions;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Security;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Authorization;

public sealed class LoginLockoutTests : MissionTestBase
{
    private const string Username = "lockout.user";
    private const string CorrectPassword = "Correct#Password1";
    private const string InvalidCredentials = "نام کاربری یا رمز عبور معتبر نیست.";
    private static SessionContext NewContext() => new(Guid.NewGuid().ToString("N"), "192.0.2.10");

    [Fact]
    public async Task FiveFailures_LockLocalAccountForFifteenMinutesAndAuditLockout()
    {
        var user = SeedLocalUser();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await AssertInvalidLogin();
        }

        Db.ChangeTracker.Clear();
        user = Db.Users.AsNoTracking().Single(item => item.Id == user.Id);
        Assert.Contains(Db.AuditEntries.AsNoTracking(), entry => entry.Action == "AccountLocked");
        Assert.Empty(Db.UserSessions.AsNoTracking().Where(item => item.Status == UserSessionStatus.Active));
        Assert.Equal(5, FailureCount(user));
        Assert.Equal(Clock.UtcNow.AddMinutes(15), LockoutEnd(user));
    }

    [Fact]
    public async Task LockedAccount_RejectsCorrectPasswordWithoutExtendingLockout()
    {
        var user = SeedLocalUser();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await AssertInvalidLogin();
        }

        var error = await Record.ExceptionAsync(() =>
            Auth().LoginLocalAsync(new LoginRequest(Username, CorrectPassword), NewContext()));

        var unauthorized = Assert.IsType<UnauthorizedAppException>(error);
        Assert.Equal(InvalidCredentials, unauthorized.Message);
        Db.ChangeTracker.Clear();
        user = Db.Users.AsNoTracking().Single(item => item.Id == user.Id);
        var lockoutEnd = LockoutEnd(user);
        await AssertInvalidLogin();
        Db.ChangeTracker.Clear();
        user = Db.Users.AsNoTracking().Single(item => item.Id == user.Id);
        Assert.Equal(lockoutEnd, LockoutEnd(user));
        Assert.Empty(Db.UserSessions.AsNoTracking().Where(item => item.Status == UserSessionStatus.Active));
    }

    [Fact]
    public async Task ExpiredLockout_AllowsLoginAndResetsFailureState()
    {
        var user = SeedLocalUser();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await AssertInvalidLogin();
        }

        Clock.NowUtc = Clock.UtcNow.AddMinutes(15);
        var result = await Auth().LoginLocalAsync(new LoginRequest(Username, CorrectPassword), NewContext());

        Assert.Equal(Username, result.Username);
        Db.ChangeTracker.Clear();
        user = Db.Users.AsNoTracking().Single(item => item.Id == user.Id);
        Assert.Equal(0, FailureCount(user));
        Assert.Null(LockoutEnd(user));
        Assert.Single(Db.UserSessions.AsNoTracking().Where(item => item.Status == UserSessionStatus.Active));
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsConsecutiveFailureCount()
    {
        var user = SeedLocalUser();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await AssertInvalidLogin();
        }

        await Auth().LoginLocalAsync(new LoginRequest(Username, CorrectPassword), NewContext());
        Db.ChangeTracker.Clear();
        user = Db.Users.AsNoTracking().Single(item => item.Id == user.Id);
        Assert.Equal(0, FailureCount(user));
        Assert.Null(LockoutEnd(user));

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await AssertInvalidLogin();
        }
        await Auth().LoginLocalAsync(new LoginRequest(Username, CorrectPassword), NewContext());
        Assert.Equal(2, Db.UserSessions.AsNoTracking().Count(item => item.Status == UserSessionStatus.Active));
    }

    [Fact]
    public async Task DisabledLocalAccount_UsesGenericCredentialError()
    {
        var user = SeedLocalUser();
        user.Disable();
        await Db.SaveChangesAsync();

        var error = await Record.ExceptionAsync(() =>
            Auth().LoginLocalAsync(new LoginRequest(Username, CorrectPassword), NewContext()));

        var unauthorized = Assert.IsType<UnauthorizedAppException>(error);
        Assert.Equal(InvalidCredentials, unauthorized.Message);
        Assert.Empty(Db.UserSessions.AsNoTracking().Where(item => item.Status == UserSessionStatus.Active));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisabledDirectoryAccount_UsesGenericCredentialError(bool ldap)
    {
        var user = new ApplicationUser(Username, "کاربر", "آزمون", isDirectoryUser: true);
        user.Disable();
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        var directory = new StubDirectory
        {
            User = new DirectoryUserInfo(Username, "کاربر آزمون", null, null, [], "CN=Test")
        };

        var error = await Record.ExceptionAsync(() => ldap
            ? Auth(directory).LoginLdapAsync(new LoginRequest(Username, "wrong"), NewContext())
            : Auth(directory).LoginWindowsAsync(Username, NewContext()));

        var unauthorized = Assert.IsType<UnauthorizedAppException>(error);
        Assert.Equal(InvalidCredentials, unauthorized.Message);
        Assert.Empty(Db.UserSessions.AsNoTracking().Where(item => item.Status == UserSessionStatus.Active));
    }

    private static int FailureCount(ApplicationUser user) => user.AccessFailedCount;

    private static DateTimeOffset? LockoutEnd(ApplicationUser user) => user.LockoutEndAtUtc;

    private ApplicationUser SeedLocalUser()
    {
        var user = new ApplicationUser(Username, "کاربر", "آزمون");
        user.SetPasswordHash(new StubPasswordHasher().Hash(CorrectPassword));
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    private async Task AssertInvalidLogin()
    {
        var error = await Record.ExceptionAsync(() =>
            Auth().LoginLocalAsync(new LoginRequest(Username, "wrong"), NewContext()));
        var unauthorized = Assert.IsType<UnauthorizedAppException>(error);
        Assert.Equal(InvalidCredentials, unauthorized.Message);
    }

    private AuthService Auth(IDirectoryAuthenticator? directory = null) => new(
        Db,
        new StubPasswordHasher(),
        directory ?? new StubDirectory(),
        new PermissionResolver(Db),
        Clock);

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string password) => passwordHash == $"hashed:{password}";
    }

    private sealed class StubDirectory : IDirectoryAuthenticator
    {
        public DirectoryUserInfo? User { get; init; }

        public Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
            => Task.FromResult(User is null
                ? DirectoryAuthResult.Failed("unused")
                : DirectoryAuthResult.Succeeded(User));

        public Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult(User);

        public Task<DirectoryProbeResult> ProbeAsync(string? sampleUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryProbeResult.Bound(0, false));

        public Task<DirectoryGroupsResult> ListGroupsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryGroupsResult.Loaded([], false));
    }
}

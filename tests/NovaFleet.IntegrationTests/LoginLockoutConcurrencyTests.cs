using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Sessions;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Security;

namespace NovaFleet.IntegrationTests;

public sealed class LoginSqlFactAttribute : FactAttribute
{
    public LoginSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVAFLEET_LOGIN_TEST_SQL")))
            Skip = "Requires the explicitly configured, isolated login SQL test database.";
    }
}

public sealed class LoginLockoutConcurrencyTests
{
    private const string DatabaseName = "NovaFleet_Login_Test_20261124";
    private const string CorrectPassword = "Correct#Password1";
    private const string NewPassword = "Changed#Password2";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly TimeProvider Time = TimeProvider.System;

    [LoginSqlFact]
    public async Task FiveConcurrentFailures_AllPersistAndLockAccount()
    {
        var username = "lockout.race." + Guid.NewGuid().ToString("N");
        await using (var seed = Open())
        {
            Assert.Empty(await seed.Database.GetPendingMigrationsAsync(Ct));
            var user = new ApplicationUser(username, "کاربر", "آزمون");
            user.SetPasswordHash(new BarrierPasswordHasher(5).Hash(CorrectPassword));
            seed.Users.Add(user);
            await seed.SaveChangesAsync(Ct);
        }

        var hasher = new BarrierPasswordHasher(5);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => AttemptAsync(username, hasher)));

        Assert.All(outcomes, error => Assert.IsType<UnauthorizedAppException>(error));
        await using var check = Open();
        var stored = await check.Users.AsNoTracking().SingleAsync(item => item.Username == username, Ct);
        Assert.Equal(5, stored.AccessFailedCount);
        Assert.NotNull(stored.LockoutEndAtUtc);
        Assert.Equal(5, await check.UserSessions.CountAsync(item => item.Username == username, Ct));
    }

    [LoginSqlFact]
    public async Task CorrectPassword_DoesNotBypassLockoutCreatedDuringVerification()
    {
        var username = "lockout.success.race." + Guid.NewGuid().ToString("N");
        await using (var seed = Open())
        {
            Assert.Empty(await seed.Database.GetPendingMigrationsAsync(Ct));
            var user = new ApplicationUser(username, "کاربر", "آزمون");
            user.SetPasswordHash($"hashed:{CorrectPassword}");
            for (var attempt = 0; attempt < 4; attempt++)
                user.RecordFailedLogin(Time.GetUtcNow());
            seed.Users.Add(user);
            await seed.SaveChangesAsync(Ct);
        }

        using var verificationStarted = new ManualResetEventSlim();
        using var releaseVerification = new ManualResetEventSlim();
        var hasher = new BlockingPasswordHasher(verificationStarted, releaseVerification);
        var login = LoginAsync(username, CorrectPassword, hasher);
        Assert.True(verificationStarted.Wait(TimeSpan.FromSeconds(15)));

        var failure = await LoginAsync(username, "wrong", new BarrierPasswordHasher(1));
        Assert.IsType<UnauthorizedAppException>(failure);
        releaseVerification.Set();
        var outcome = await login;

        var unauthorized = Assert.IsType<UnauthorizedAppException>(outcome);
        Assert.Equal("نام کاربری یا رمز عبور معتبر نیست.", unauthorized.Message);
        await using var check = Open();
        var stored = await check.Users.AsNoTracking().SingleAsync(item => item.Username == username, Ct);
        Assert.Equal(5, stored.AccessFailedCount);
        Assert.NotNull(stored.LockoutEndAtUtc);
        Assert.Empty(await check.UserSessions.Where(item => item.Username == username && item.Status == UserSessionStatus.Active).ToListAsync(Ct));
    }

    [LoginSqlFact]
    public async Task PasswordResetDuringOldPasswordLogin_CannotLeaveActiveSession()
    {
        await AssertPasswordMutationRejectsConcurrentLoginAsync(resetPassword: true);
    }

    [LoginSqlFact]
    public async Task PasswordChangeDuringOldPasswordLogin_CannotLeaveActiveSession()
    {
        await AssertPasswordMutationRejectsConcurrentLoginAsync(resetPassword: false);
    }

    private static async Task AssertPasswordMutationRejectsConcurrentLoginAsync(bool resetPassword)
    {
        var username = (resetPassword ? "reset.session.race." : "change.session.race.") + Guid.NewGuid().ToString("N");
        await using (var seed = Open())
        {
            Assert.Empty(await seed.Database.GetPendingMigrationsAsync(Ct));
            var user = new ApplicationUser(username, "کاربر", "آزمون");
            user.SetPasswordHash($"hashed:{CorrectPassword}");
            seed.Users.Add(user);
            seed.UserSessions.Add(new UserSession(username, UserSessionLoginMethod.Local, "192.0.2.20", Guid.NewGuid()));
            await seed.SaveChangesAsync(Ct);
        }

        using var verificationStarted = new ManualResetEventSlim();
        using var releaseVerification = new ManualResetEventSlim();
        var mutationUpdate = new PauseUserUpdateInterceptor();
        var loginUpdate = new UserUpdateStartedInterceptor();
        var login = LoginAsync(username, CorrectPassword, new BlockingPasswordHasher(verificationStarted, releaseVerification), loginUpdate);
        Assert.True(verificationStarted.Wait(TimeSpan.FromSeconds(15)));

        var mutation = MutatePasswordAsync(username, resetPassword, mutationUpdate);
        await mutationUpdate.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        releaseVerification.Set();
        await loginUpdate.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await login.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
        }

        mutationUpdate.Release();

        var outcome = await login;
        await mutation;

        Assert.IsType<UnauthorizedAppException>(outcome);
        await using var check = Open();
        Assert.Empty(await check.UserSessions
            .Where(item => item.Username == username && item.Status == UserSessionStatus.Active)
            .ToListAsync(Ct));
    }

    private static async Task MutatePasswordAsync(string username, bool resetPassword, DbCommandInterceptor interceptor)
    {
        await using var db = Open(interceptor);
        var userId = await db.Users.AsNoTracking().Where(item => item.Username == username).Select(item => item.Id).SingleAsync(Ct);
        var hasher = new StubPasswordHasher();
        if (resetPassword)
        {
            var service = new UserService(db, hasher, new TestCurrentUser("admin"), new FakeAuditTrail());
            await service.ResetPasswordAsync(userId, new ResetPasswordRequest(NewPassword), Ct);
            return;
        }

        var auth = new AuthService(db, hasher, new FailedDirectory(), new PermissionResolver(db), new FixedClock());
        await auth.ChangePasswordAsync(username, CorrectPassword, NewPassword, Ct);
    }

    private static Task<Exception?> AttemptAsync(string username, IPasswordHasher hasher) =>
        LoginAsync(username, "wrong", hasher);

    private static async Task<Exception?> LoginAsync(
        string username,
        string password,
        IPasswordHasher hasher,
        DbCommandInterceptor? interceptor = null)
    {
        await using var db = Open(interceptor);
        var auth = new AuthService(
            db,
            hasher,
            new FailedDirectory(),
            new PermissionResolver(db),
            new FixedClock());
        try
        {
            await auth.LoginLocalAsync(
                new LoginRequest(username, password),
                new SessionContext(Guid.NewGuid().ToString("N"), "192.0.2.20"),
                Ct);
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static NovaFleetDbContext Open(DbCommandInterceptor? interceptor = null)
    {
        var connection = Environment.GetEnvironmentVariable("NOVAFLEET_LOGIN_TEST_SQL")
            ?? throw new InvalidOperationException("An isolated login SQL connection is required.");
        var parsed = new SqlConnectionStringBuilder(connection);
        var isLocalHost = string.Equals(parsed.DataSource, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsed.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase);
        if (!isLocalHost || parsed.InitialCatalog != DatabaseName)
            throw new InvalidOperationException("Only the approved local login test database is allowed.");
        var options = new DbContextOptionsBuilder<NovaFleetDbContext>().UseSqlServer(connection);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new NovaFleetDbContext(options.Options);
    }

    private sealed class TestCurrentUser(string username) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string Username => username;
        public string DisplayName => username;
        public bool IsLocalAdmin => false;
        public EffectivePermissionSet Permissions { get; } = EffectivePermissionSet.Build(
            [(PermissionCatalog.Administration.UsersManage, PermissionEffect.Grant)]);
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string passwordHash, string password) => passwordHash == $"hashed:{password}";
    }

    private sealed class FakeAuditTrail : IAuditTrail
    {
        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private class UserUpdateInterceptor : DbCommandInterceptor
    {
        protected static bool IsUserUpdate(DbCommand command) =>
            command.CommandText.Contains("UPDATE [Users]", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UserUpdateStartedInterceptor : UserUpdateInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsUserUpdate(command))
                Started.TrySetResult();
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PauseUserUpdateInterceptor : UserUpdateInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsUserUpdate(command))
            {
                Started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Time.GetUtcNow();
    }

    private sealed class BarrierPasswordHasher(int participants) : IPasswordHasher
    {
        private readonly Barrier barrier = new(participants);

        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string password)
        {
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(15)));
            return passwordHash == $"hashed:{password}";
        }
    }

    private sealed class BlockingPasswordHasher(
        ManualResetEventSlim verificationStarted,
        ManualResetEventSlim releaseVerification) : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string password)
        {
            if (password == CorrectPassword)
            {
                verificationStarted.Set();
                Assert.True(releaseVerification.Wait(TimeSpan.FromSeconds(15)));
            }

            return passwordHash == $"hashed:{password}";
        }
    }

    private sealed class FailedDirectory : IDirectoryAuthenticator
    {
        public Task<DirectoryAuthResult> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryAuthResult.Failed("unused"));

        public Task<DirectoryUserInfo?> FindByUsernameAsync(
            string username,
            CancellationToken cancellationToken = default)
            => Task.FromResult<DirectoryUserInfo?>(null);

        public Task<DirectoryProbeResult> ProbeAsync(
            string? sampleUsername,
            CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryProbeResult.Bound(0, false));

        public Task<DirectoryGroupsResult> ListGroupsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryGroupsResult.Loaded([], false));
    }
}

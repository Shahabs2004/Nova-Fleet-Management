using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Security;

namespace NovaFleet.IntegrationTests;

public sealed class ProtectedAdministratorConcurrencyTests
{
    private const string DatabaseName = "NovaFleet_Login_Test_20261124";
    private static readonly CancellationToken Ct = CancellationToken.None;

    [LoginSqlFact]
    public async Task ProtectedRoleAssignmentDuringOrdinaryUpdate_CannotBeRemovedByStaleAuthorization()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var managerUsername = "race.manager." + suffix;
        var adminUsername = "race.admin." + suffix;
        var targetUsername = "race.target." + suffix;
        Guid systemRoleId;
        Guid operatorRoleId;
        Guid targetId;

        await using (var seed = Open())
        {
            Assert.Empty(await seed.Database.GetPendingMigrationsAsync(Ct));
            var systemRole = await seed.Roles.FirstOrDefaultAsync(item => item.Name == "SystemAdministrator", Ct);
            if (systemRole is null)
            {
                systemRole = new Role("SystemAdministrator", "مدیر سامانه", null, true);
                seed.Roles.Add(systemRole);
            }

            var operatorRole = await seed.Roles.FirstOrDefaultAsync(item => item.Name == "Operator", Ct);
            if (operatorRole is null)
            {
                operatorRole = new Role("Operator", "اپراتور", null, true);
                seed.Roles.Add(operatorRole);
            }

            seed.Users.AddRange(
                new ApplicationUser(managerUsername, "مدیر", "کاربران"),
                new ApplicationUser(adminUsername, "مدیر", "محلی", isLocalAdmin: true));
            var target = new ApplicationUser(targetUsername, "هدف", "آزمون");
            seed.Users.Add(target);
            seed.UserRoles.Add(new UserRole(target.Id, operatorRole.Id));
            await seed.SaveChangesAsync(Ct);
            systemRoleId = systemRole.Id;
            operatorRoleId = operatorRole.Id;
            targetId = target.Id;
        }

        var roleReadInterceptor = new PauseSecondUserRoleReadInterceptor();
        await using var ordinaryDb = Open(roleReadInterceptor);
        await using var adminDb = Open();
        var ordinaryService = new UserService(
            ordinaryDb, new StubPasswordHasher(), new TestCurrentUser(managerUsername), new FakeAuditTrail());
        var ordinaryUpdate = Task.Run(() => ordinaryService.UpdateAsync(
            targetId,
            new UpdateUserRequest("هدف", "آزمون", null, null, null, [operatorRoleId], null),
            Ct));

        Task adminUpdate = Task.CompletedTask;
        try
        {
            await Task.WhenAny(roleReadInterceptor.Started.Task, ordinaryUpdate, Task.Delay(TimeSpan.FromSeconds(15)))
                .ConfigureAwait(false);
            if (ordinaryUpdate.IsCompleted)
                await ordinaryUpdate.ConfigureAwait(false);
            Assert.True(roleReadInterceptor.Started.Task.IsCompleted);

            var adminService = new UserService(
                adminDb,
                new StubPasswordHasher(),
                new TestCurrentUser(adminUsername) { IsLocalAdmin = true },
                new FakeAuditTrail());
            adminUpdate = Task.Run(() => adminService.UpdateAsync(
                targetId,
                new UpdateUserRequest("هدف", "آزمون", null, null, null, [systemRoleId], null),
                Ct));
            await Task.WhenAny(adminUpdate, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        }
        finally
        {
            roleReadInterceptor.Release();
        }

        await WaitForCompletionAsync(ordinaryUpdate, TimeSpan.FromSeconds(20));
        await WaitForCompletionAsync(adminUpdate, TimeSpan.FromSeconds(20));

        await using var check = Open();
        var assignedRoleIds = await check.UserRoles.AsNoTracking()
            .Where(item => item.UserId == targetId)
            .Select(item => item.RoleId)
            .ToListAsync(Ct);
        Assert.Contains(systemRoleId, assignedRoleIds);
    }

    private static async Task WaitForCompletionAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        Assert.Same(task, completed);
        await task.ConfigureAwait(false);
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
        public bool IsLocalAdmin { get; init; }
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

    private sealed class PauseSecondUserRoleReadInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource release = new();
        private int userRoleReads;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [UserRoles]", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref userRoleReads) == 2)
            {
                Started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NovaFleet.Infrastructure.Persistence.Migrations;

namespace NovaFleet.UnitTests.Authorization;

public sealed class LoginLockoutMigrationTests
{
    [Fact]
    public void Down_RevokesBothFleetManagerSessionPermissions()
    {
        var migration = new TestableAddLoginLockout();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        migration.ApplyDown(builder);

        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("security.sessions.view", sql);
        Assert.Contains("security.sessions.manage", sql);
    }

    private sealed class TestableAddLoginLockout : AddLoginLockout
    {
        public void ApplyDown(MigrationBuilder migrationBuilder) => Down(migrationBuilder);
    }
}

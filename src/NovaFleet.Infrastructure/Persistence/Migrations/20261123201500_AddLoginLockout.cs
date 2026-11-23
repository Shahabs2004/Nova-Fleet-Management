using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEndAtUtc",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, Permission, Effect)
                SELECT r.Id, p.Permission, 1
                FROM Roles r
                CROSS JOIN (VALUES
                    (N'security.sessions.view'),
                    (N'security.sessions.manage')
                ) AS p(Permission)
                WHERE r.Name = N'FleetManager'
                  AND NOT EXISTS (
                      SELECT 1 FROM RolePermissions rp
                      WHERE rp.RoleId = r.Id AND rp.Permission = p.Permission
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE rp
                FROM RolePermissions rp
                INNER JOIN Roles r ON r.Id = rp.RoleId
                WHERE r.Name = N'FleetManager'
                  AND rp.Permission IN (N'security.sessions.view', N'security.sessions.manage');
                """);

            migrationBuilder.DropColumn(
                name: "LockoutEndAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                table: "Users");
        }
    }
}

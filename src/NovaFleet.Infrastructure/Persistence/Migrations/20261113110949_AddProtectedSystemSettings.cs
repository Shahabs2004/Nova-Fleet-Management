using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProtectedSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSecret",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedValue",
                table: "SystemSettings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSecret",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ProtectedValue",
                table: "SystemSettings");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMissionBackdatedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBackdated",
                table: "Missions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBackdated",
                table: "Missions");
        }
    }
}

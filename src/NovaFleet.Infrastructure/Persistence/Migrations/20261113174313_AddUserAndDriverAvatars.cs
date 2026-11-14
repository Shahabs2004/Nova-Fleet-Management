using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAndDriverAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarDataUrl",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarDataUrl",
                table: "Drivers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarDataUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarDataUrl",
                table: "Drivers");
        }
    }
}

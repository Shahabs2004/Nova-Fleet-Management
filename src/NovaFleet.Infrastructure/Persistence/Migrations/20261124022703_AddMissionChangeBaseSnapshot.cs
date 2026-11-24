using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMissionChangeBaseSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BasePlannedEndAtUtc",
                table: "MissionChangeRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BasePlannedStartAtUtc",
                table: "MissionChangeRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "BaseRowVersion",
                table: "MissionChangeRequests",
                type: "varbinary(8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaseStatus",
                table: "MissionChangeRequests",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BasePlannedEndAtUtc",
                table: "MissionChangeRequests");

            migrationBuilder.DropColumn(
                name: "BasePlannedStartAtUtc",
                table: "MissionChangeRequests");

            migrationBuilder.DropColumn(
                name: "BaseRowVersion",
                table: "MissionChangeRequests");

            migrationBuilder.DropColumn(
                name: "BaseStatus",
                table: "MissionChangeRequests");
        }
    }
}

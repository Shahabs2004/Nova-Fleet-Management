using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropMissionRequestingGroupOptionalPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.MissionRequestingGroupArchive', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MissionRequestingGroupArchive (
        MissionId uniqueidentifier NOT NULL PRIMARY KEY,
        RequestingGroup nvarchar(200) NOT NULL
    );
END
INSERT INTO dbo.MissionRequestingGroupArchive (MissionId, RequestingGroup)
SELECT m.Id, m.RequestingGroup
FROM dbo.Missions m
WHERE m.RequestingGroup IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.MissionRequestingGroupArchive a WHERE a.MissionId = m.Id);
");

            migrationBuilder.DropColumn(
                name: "RequestingGroup",
                table: "Missions");

            migrationBuilder.AlterColumn<string>(
                name: "Purpose",
                table: "Missions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Separate batches: SQL Server resolves columns before a conditional ALTER TABLE executes.
            migrationBuilder.Sql("UPDATE dbo.Missions SET Purpose = N'' WHERE Purpose IS NULL;");
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Missions', N'RequestingGroup') IS NULL
BEGIN
    ALTER TABLE dbo.Missions ADD RequestingGroup nvarchar(200) NULL;
END
");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.MissionRequestingGroupArchive', N'U') IS NOT NULL
BEGIN
    UPDATE m SET m.RequestingGroup = a.RequestingGroup
    FROM dbo.Missions m
    INNER JOIN dbo.MissionRequestingGroupArchive a ON a.MissionId = m.Id;
END
");
            migrationBuilder.Sql("UPDATE dbo.Missions SET RequestingGroup = N'' WHERE RequestingGroup IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Purpose",
                table: "Missions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RequestingGroup",
                table: "Missions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.MissionRequestingGroupArchive', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.MissionRequestingGroupArchive;
END
");
        }
    }
}

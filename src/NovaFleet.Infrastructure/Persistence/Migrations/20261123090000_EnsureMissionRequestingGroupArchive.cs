using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnsureMissionRequestingGroupArchive : Migration
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
IF COL_LENGTH(N'dbo.Missions', N'RequestingGroup') IS NOT NULL
BEGIN
    -- Dynamic SQL avoids compile-time failure when the historical drop already removed the column.
    EXEC(N'
INSERT INTO dbo.MissionRequestingGroupArchive (MissionId, RequestingGroup)
SELECT m.Id, m.RequestingGroup
FROM dbo.Missions m
WHERE m.RequestingGroup IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.MissionRequestingGroupArchive a WHERE a.MissionId = m.Id);
');
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

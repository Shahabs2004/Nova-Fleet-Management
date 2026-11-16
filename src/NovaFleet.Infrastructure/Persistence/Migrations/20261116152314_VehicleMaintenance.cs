using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VehicleMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PerformedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Workshop = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OtherCostToman = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecords_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityKind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssetKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IntervalKm = table.Column<long>(type: "bigint", nullable: true),
                    IntervalMonths = table.Column<int>(type: "int", nullable: true),
                    WarningKm = table.Column<int>(type: "int", nullable: false),
                    WarningDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleOdometerReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsTimeEstimated = table.Column<bool>(type: "bit", nullable: false),
                    Kilometers = table.Column<long>(type: "bigint", nullable: false),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleOdometerReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleOdometerReadings_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceAttachments_MaintenanceRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "MaintenanceRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IntervalKm = table.Column<long>(type: "bigint", nullable: true),
                    IntervalMonths = table.Column<int>(type: "int", nullable: true),
                    WarningKm = table.Column<int>(type: "int", nullable: false),
                    WarningDays = table.Column<int>(type: "int", nullable: false),
                    BaselineKm = table.Column<long>(type: "bigint", nullable: true),
                    BaselineAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceSchedules_MaintenanceTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "MaintenanceTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceSchedules_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecordItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssetKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LaborCostToman = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecordItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecordItems_MaintenanceRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "MaintenanceRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecordItems_MaintenanceSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "MaintenanceSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecordItems_MaintenanceTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "MaintenanceTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceReminderDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceReminderDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceReminderDeliveries_MaintenanceSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "MaintenanceSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecordParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Specification = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitCostToman = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    WarrantyUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecordParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecordParts_MaintenanceRecordItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "MaintenanceRecordItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceAttachments_RecordId",
                table: "MaintenanceAttachments",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecordItems_RecordId",
                table: "MaintenanceRecordItems",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecordItems_ScheduleId",
                table: "MaintenanceRecordItems",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecordItems_TemplateId",
                table: "MaintenanceRecordItems",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecordParts_ItemId",
                table: "MaintenanceRecordParts",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_VehicleId_PerformedAtUtc",
                table: "MaintenanceRecords",
                columns: new[] { "VehicleId", "PerformedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_VehicleId_RequestId",
                table: "MaintenanceRecords",
                columns: new[] { "VehicleId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceReminderDeliveries_ScheduleId_CycleKey_Stage_Recipient",
                table: "MaintenanceReminderDeliveries",
                columns: new[] { "ScheduleId", "CycleKey", "Stage", "Recipient" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRevisions_EntityKind_EntityId_Version",
                table: "MaintenanceRevisions",
                columns: new[] { "EntityKind", "EntityId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRevisions_VehicleId_CreatedAtUtc",
                table: "MaintenanceRevisions",
                columns: new[] { "VehicleId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSchedules_TemplateId",
                table: "MaintenanceSchedules",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSchedules_VehicleId_TemplateId",
                table: "MaintenanceSchedules",
                columns: new[] { "VehicleId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTemplates_Name",
                table: "MaintenanceTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleOdometerReadings_SourceKind_SourceId",
                table: "VehicleOdometerReadings",
                columns: new[] { "SourceKind", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleOdometerReadings_VehicleId_ObservedAtUtc",
                table: "VehicleOdometerReadings",
                columns: new[] { "VehicleId", "ObservedAtUtc" });

            migrationBuilder.Sql("""
                INSERT INTO [VehicleOdometerReadings]
                    ([Id], [VehicleId], [SourceKind], [SourceId], [ObservedAtUtc], [IsTimeEstimated],
                     [Kilometers], [IsVoided], [RecordedBy], [Version], [CreatedAtUtc])
                SELECT NEWID(), [VehicleId], N'mission-start', [Id],
                       COALESCE([ActualStartAtUtc], [PlannedStartAtUtc]),
                       CASE WHEN [ActualStartAtUtc] IS NULL THEN 1 ELSE 0 END,
                       [OdometerStartKm], 0, N'legacy-migration', 1, SYSDATETIMEOFFSET()
                FROM [Missions] WHERE [VehicleId] IS NOT NULL AND [OdometerStartKm] IS NOT NULL;

                INSERT INTO [VehicleOdometerReadings]
                    ([Id], [VehicleId], [SourceKind], [SourceId], [ObservedAtUtc], [IsTimeEstimated],
                     [Kilometers], [IsVoided], [RecordedBy], [Version], [CreatedAtUtc])
                SELECT NEWID(), [VehicleId], N'mission-end', [Id],
                       COALESCE([ActualEndAtUtc], [PlannedEndAtUtc]),
                       CASE WHEN [ActualEndAtUtc] IS NULL THEN 1 ELSE 0 END,
                       [OdometerEndKm], 0, N'legacy-migration', 1, SYSDATETIMEOFFSET()
                FROM [Missions] WHERE [VehicleId] IS NOT NULL AND [OdometerEndKm] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceAttachments");

            migrationBuilder.DropTable(
                name: "MaintenanceRecordParts");

            migrationBuilder.DropTable(
                name: "MaintenanceReminderDeliveries");

            migrationBuilder.DropTable(
                name: "MaintenanceRevisions");

            migrationBuilder.DropTable(
                name: "VehicleOdometerReadings");

            migrationBuilder.DropTable(
                name: "MaintenanceRecordItems");

            migrationBuilder.DropTable(
                name: "MaintenanceRecords");

            migrationBuilder.DropTable(
                name: "MaintenanceSchedules");

            migrationBuilder.DropTable(
                name: "MaintenanceTemplates");
        }
    }
}

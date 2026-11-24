using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaFleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsQueueSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GatewayMode",
                table: "SmsQueueItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextDeliveryPollAtUtc",
                table: "SmsQueueItems",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SendStartedAtUtc",
                table: "SmsQueueItems",
                type: "datetimeoffset",
                nullable: true);

            // Historical accepted rows predate per-item gateway mode. Polling them as the
            // default Simulation mode could falsely mark a real paid message delivered.
            migrationBuilder.Sql("""
                UPDATE [SmsQueueItems]
                SET [Status] = 9,
                    [LastError] = N'حالت درگاه پیام پذیرفته‌شده پیش از ارتقا ثبت نشده است؛ نتیجه نیازمند بررسی اپراتور است.',
                    [NextAttemptAtUtc] = NULL,
                    [NextDeliveryPollAtUtc] = NULL,
                    [UpdatedAtUtc] = SYSDATETIMEOFFSET()
                WHERE [Status] = 3;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SmsQueueItems_Status_NextDeliveryPollAtUtc",
                table: "SmsQueueItems",
                columns: new[] { "Status", "NextDeliveryPollAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsQueueItems_Status_SendStartedAtUtc",
                table: "SmsQueueItems",
                columns: new[] { "Status", "SendStartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SmsQueueItems_Status_NextDeliveryPollAtUtc",
                table: "SmsQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_SmsQueueItems_Status_SendStartedAtUtc",
                table: "SmsQueueItems");

            migrationBuilder.DropColumn(
                name: "GatewayMode",
                table: "SmsQueueItems");

            migrationBuilder.DropColumn(
                name: "NextDeliveryPollAtUtc",
                table: "SmsQueueItems");

            migrationBuilder.DropColumn(
                name: "SendStartedAtUtc",
                table: "SmsQueueItems");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiffin.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.EnsureSchema(
                name: "idempotency");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessageKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Arguments = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "processed_messages",
                schema: "idempotency",
                columns: table => new
                {
                    Consumer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_messages", x => new { x.Consumer, x.MessageId });
                });

            migrationBuilder.CreateTable(
                name: "requests",
                schema: "idempotency",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Operation = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Response = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requests", x => new { x.Scope, x.Key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_city_recipient_time",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "City", "RecipientId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_processed_messages_ProcessedOnUtc",
                schema: "idempotency",
                table: "processed_messages",
                column: "ProcessedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_requests_CreatedOnUtc",
                schema: "idempotency",
                table: "requests",
                column: "CreatedOnUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "processed_messages",
                schema: "idempotency");

            migrationBuilder.DropTable(
                name: "requests",
                schema: "idempotency");
        }
    }
}

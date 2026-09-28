using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiffin.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dispatch");

            migrationBuilder.EnsureSchema(
                name: "idempotency");

            migrationBuilder.CreateTable(
                name: "couriers",
                schema: "dispatch",
                columns: table => new
                {
                    CourierId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    City = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsOnDuty = table.Column<bool>(type: "boolean", nullable: false),
                    CarryingOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    FreeSinceUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_couriers", x => x.CourierId);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "dispatch",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CourierId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CourierName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RestaurantName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    To_Recipient = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    To_Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    To_District = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    To_Line = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AssignedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.OrderId);
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
                name: "ix_couriers_roster",
                schema: "dispatch",
                table: "couriers",
                columns: new[] { "City", "IsOnDuty", "FreeSinceUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_city_courier_status",
                schema: "dispatch",
                table: "deliveries",
                columns: new[] { "City", "CourierId", "Status" });

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
                name: "couriers",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "processed_messages",
                schema: "idempotency");

            migrationBuilder.DropTable(
                name: "requests",
                schema: "idempotency");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tiffin.Kitchen.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialKitchen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "idempotency");

            migrationBuilder.EnsureSchema(
                name: "kitchen");

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorKind = table.Column<int>(type: "integer", nullable: false),
                    ActorSubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActorClientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActorUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Module = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    FailureDomain = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OperationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Changes = table.Column<string>(type: "jsonb", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entries", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "restaurants",
                schema: "kitchen",
                columns: table => new
                {
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ManagerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurants", x => x.RestaurantId);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "kitchen",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReadyInMinutes = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReceivedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "ticket_lines",
                schema: "kitchen",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_lines", x => new { x.OrderId, x.Code });
                    table.ForeignKey(
                        name: "FK_ticket_lines_tickets_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "kitchen",
                        principalTable: "tickets",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entries_ActorSubjectId",
                schema: "audit",
                table: "entries",
                column: "ActorSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_entries_CorrelationId",
                schema: "audit",
                table: "entries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_entries_EntityType_EntityId",
                schema: "audit",
                table: "entries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_entries_OccurredAtUtc",
                schema: "audit",
                table: "entries",
                column: "OccurredAtUtc");

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

            migrationBuilder.CreateIndex(
                name: "ix_restaurants_city_manager",
                schema: "kitchen",
                table: "restaurants",
                columns: new[] { "City", "ManagerId" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_city_restaurant_status",
                schema: "kitchen",
                table: "tickets",
                columns: new[] { "City", "RestaurantId", "Status", "ReceivedOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entries",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "processed_messages",
                schema: "idempotency");

            migrationBuilder.DropTable(
                name: "requests",
                schema: "idempotency");

            migrationBuilder.DropTable(
                name: "restaurants",
                schema: "kitchen");

            migrationBuilder.DropTable(
                name: "ticket_lines",
                schema: "kitchen");

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "kitchen");
        }
    }
}

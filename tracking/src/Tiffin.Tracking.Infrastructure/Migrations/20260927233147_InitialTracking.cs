using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MPCore.Persistence.Timescale;

#nullable disable

namespace Tiffin.Tracking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tracking");

            migrationBuilder.EnsureSchema(
                name: "idempotency");

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "tracking",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    city = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    order_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    courier_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    last_latitude = table.Column<double>(type: "double precision", nullable: true),
                    last_longitude = table.Column<double>(type: "double precision", nullable: true),
                    last_seen_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    position_count = table.Column<int>(type: "integer", nullable: false),
                    began_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    arrived_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    recorded_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.order_id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "tracking",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    city = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    courier_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => new { x.order_id, x.recorded_on_utc });
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
                name: "IX_processed_messages_ProcessedOnUtc",
                schema: "idempotency",
                table: "processed_messages",
                column: "ProcessedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_requests_CreatedOnUtc",
                schema: "idempotency",
                table: "requests",
                column: "CreatedOnUtc");

            // Where a courier was is a time series. TimescaleDB partitions it into one chunk per day on the
            // time of the measurement, keeps thirty days of it, and compresses what is older than a week,
            // one segment per order. These steps are written by hand: Entity Framework does not know what a
            // hypertable is.
            migrationBuilder.EnsureTimescale();
            migrationBuilder.CreateHypertable("positions", "recorded_on_utc", schema: "tracking", chunkInterval: "1 day");
            migrationBuilder.AddCompression("positions", orderBy: "recorded_on_utc", compressOlderThan: "7 days", segmentBy: "order_id", schema: "tracking");
            migrationBuilder.AddRetentionPolicy("positions", olderThan: "30 days", schema: "tracking");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "tracking");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "tracking");

            migrationBuilder.DropTable(
                name: "processed_messages",
                schema: "idempotency");

            migrationBuilder.DropTable(
                name: "requests",
                schema: "idempotency");
        }
    }
}

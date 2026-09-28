using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tiffin.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "media");

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
                name: "files",
                schema: "media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReservedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.Id);
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
                name: "ix_files_city_owner",
                schema: "media",
                table: "files",
                columns: new[] { "City", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "ix_files_places_to_sweep",
                schema: "media",
                table: "files",
                column: "ExpiresOnUtc",
                filter: "\"State\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_files_storage_key",
                schema: "media",
                table: "files",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entries",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "files",
                schema: "media");
        }
    }
}

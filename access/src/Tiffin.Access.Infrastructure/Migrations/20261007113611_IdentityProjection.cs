using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiffin.Access.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdentityProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_people",
                schema: "access",
                columns: table => new
                {
                    PersonId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    City = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Roles = table.Column<string[]>(type: "text[]", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastEventId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LastEventOccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_people", x => x.PersonId);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_people_city_username",
                schema: "access",
                table: "identity_people",
                columns: new[] { "City", "UserName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_people",
                schema: "access");
        }
    }
}

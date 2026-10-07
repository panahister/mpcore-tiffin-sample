using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiffin.Restaurants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MenuItemPicture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PictureId",
                schema: "restaurants",
                table: "menu_items",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PictureId",
                schema: "restaurants",
                table: "menu_items");
        }
    }
}

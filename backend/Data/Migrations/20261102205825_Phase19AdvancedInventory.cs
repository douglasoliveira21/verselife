using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase19AdvancedInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_favorite",
                schema: "inventory",
                table: "items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_items_owner_user_id_is_favorite_created_at",
                schema: "inventory",
                table: "items",
                columns: new[] { "owner_user_id", "is_favorite", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_items_owner_user_id_is_favorite_created_at",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "is_favorite",
                schema: "inventory",
                table: "items");
        }
    }
}

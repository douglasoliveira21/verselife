using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase65IslandManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_rating",
                schema: "world",
                table: "islands",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "livre");

            migrationBuilder.AddColumn<string>(
                name: "language",
                schema: "world",
                table: "islands",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "pt-BR");

            migrationBuilder.AddColumn<bool>(
                name: "show_in_search",
                schema: "world",
                table: "islands",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_on_map",
                schema: "world",
                table: "islands",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content_rating",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "language",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "show_in_search",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "show_on_map",
                schema: "world",
                table: "islands");
        }
    }
}

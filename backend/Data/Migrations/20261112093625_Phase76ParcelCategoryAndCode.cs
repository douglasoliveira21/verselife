using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase76ParcelCategoryAndCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "world",
                table: "parcels",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "residencial");

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "world",
                table: "parcels",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "world",
                table: "parcels");
        }
    }
}

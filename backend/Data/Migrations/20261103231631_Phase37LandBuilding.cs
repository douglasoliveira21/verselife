using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase37LandBuilding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auto_return_minutes",
                schema: "world",
                table: "parcels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "build_mode",
                schema: "world",
                table: "parcels",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "owner");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_return_minutes",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "build_mode",
                schema: "world",
                table: "parcels");
        }
    }
}

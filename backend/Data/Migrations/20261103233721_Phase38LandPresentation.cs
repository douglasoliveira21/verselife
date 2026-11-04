using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase38LandPresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "arrival_yaw",
                schema: "world",
                table: "parcels",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<string>(
                name: "media_json",
                schema: "world",
                table: "parcels",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "teleport_mode",
                schema: "world",
                table: "parcels",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "free");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "arrival_yaw",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "media_json",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "teleport_mode",
                schema: "world",
                table: "parcels");
        }
    }
}

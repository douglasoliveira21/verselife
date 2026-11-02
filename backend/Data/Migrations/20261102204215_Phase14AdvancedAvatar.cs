using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase14AdvancedAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attachments_json",
                schema: "identity",
                table: "avatar_appearances",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "beard_style",
                schema: "identity",
                table: "avatar_appearances",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<string>(
                name: "eye_color",
                schema: "identity",
                table: "avatar_appearances",
                type: "character varying(9)",
                maxLength: 9,
                nullable: false,
                defaultValue: "#4A3326");

            migrationBuilder.AddColumn<string>(
                name: "face_settings_json",
                schema: "identity",
                table: "avatar_appearances",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "outfits_json",
                schema: "identity",
                table: "avatar_appearances",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachments_json",
                schema: "identity",
                table: "avatar_appearances");

            migrationBuilder.DropColumn(
                name: "beard_style",
                schema: "identity",
                table: "avatar_appearances");

            migrationBuilder.DropColumn(
                name: "eye_color",
                schema: "identity",
                table: "avatar_appearances");

            migrationBuilder.DropColumn(
                name: "face_settings_json",
                schema: "identity",
                table: "avatar_appearances");

            migrationBuilder.DropColumn(
                name: "outfits_json",
                schema: "identity",
                table: "avatar_appearances");
        }
    }
}

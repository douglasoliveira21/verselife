using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase40AvatarModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "custom_model_id",
                schema: "identity",
                table: "avatars",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "avatar_models",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    triangles = table.Column<int>(type: "integer", nullable: false),
                    joints = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<float>(type: "real", nullable: false),
                    scale_to_standard = table.Column<float>(type: "real", nullable: false),
                    report_json = table.Column<string>(type: "jsonb", nullable: false),
                    bone_map_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avatar_models", x => x.id);
                    table.ForeignKey(
                        name: "FK_avatar_models_avatars_avatar_id",
                        column: x => x.avatar_id,
                        principalSchema: "identity",
                        principalTable: "avatars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_avatar_models_avatar_id_content_hash",
                schema: "identity",
                table: "avatar_models",
                columns: new[] { "avatar_id", "content_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avatar_models",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "custom_model_id",
                schema: "identity",
                table: "avatars");
        }
    }
}

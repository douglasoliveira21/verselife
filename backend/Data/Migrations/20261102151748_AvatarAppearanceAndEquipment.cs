using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AvatarAppearanceAndEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.AddColumn<string>(
                name: "base_model",
                schema: "identity",
                table: "avatars",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "human_a");

            migrationBuilder.CreateTable(
                name: "avatar_appearances",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skin_color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    hair_color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    hair_style = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    body_settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    camera_sensitivity = table.Column<float>(type: "real", nullable: false),
                    invert_camera_y = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avatar_appearances", x => x.id);
                    table.ForeignKey(
                        name: "FK_avatar_appearances_avatars_avatar_id",
                        column: x => x.avatar_id,
                        principalSchema: "identity",
                        principalTable: "avatars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "equipped_items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    item_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tint_color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    equipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipped_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_equipped_items_avatars_avatar_id",
                        column: x => x.avatar_id,
                        principalSchema: "identity",
                        principalTable: "avatars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_avatar_appearances_avatar_id",
                schema: "identity",
                table: "avatar_appearances",
                column: "avatar_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipped_items_avatar_id_slot",
                schema: "inventory",
                table: "equipped_items",
                columns: new[] { "avatar_id", "slot" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO identity.avatar_appearances
                    (id, avatar_id, skin_color, hair_color, hair_style, body_settings_json, camera_sensitivity, invert_camera_y, created_at, updated_at)
                SELECT gen_random_uuid(), id, '#C98F65', '#2B1B14', 'short', '{}', 0.003, false, created_at, updated_at
                FROM identity.avatars
                ON CONFLICT (avatar_id) DO NOTHING;

                INSERT INTO inventory.equipped_items (id, avatar_id, slot, item_key, tint_color, equipped_at)
                SELECT gen_random_uuid(), id, 'top', 'tee_basic', '#3AAFC1', updated_at FROM identity.avatars
                UNION ALL
                SELECT gen_random_uuid(), id, 'bottom', 'pants_basic', '#263B56', updated_at FROM identity.avatars
                UNION ALL
                SELECT gen_random_uuid(), id, 'shoes', 'sneakers_basic', '#E7E9EE', updated_at FROM identity.avatars
                ON CONFLICT (avatar_id, slot) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avatar_appearances",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "equipped_items",
                schema: "inventory");

            migrationBuilder.DropColumn(
                name: "base_model",
                schema: "identity",
                table: "avatars");
        }
    }
}

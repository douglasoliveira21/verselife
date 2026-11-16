using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase99AvatarAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "avatar_attachments",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_point = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    position_x = table.Column<double>(type: "double precision", nullable: false),
                    position_y = table.Column<double>(type: "double precision", nullable: false),
                    position_z = table.Column<double>(type: "double precision", nullable: false),
                    rotation_x = table.Column<double>(type: "double precision", nullable: false),
                    rotation_y = table.Column<double>(type: "double precision", nullable: false),
                    rotation_z = table.Column<double>(type: "double precision", nullable: false),
                    scale_x = table.Column<double>(type: "double precision", nullable: false),
                    scale_y = table.Column<double>(type: "double precision", nullable: false),
                    scale_z = table.Column<double>(type: "double precision", nullable: false),
                    equipped = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    skeleton_profile_version = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avatar_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_avatar_attachments_avatars_avatar_id",
                        column: x => x.avatar_id,
                        principalSchema: "identity",
                        principalTable: "avatars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_avatar_attachments_items_inventory_item_id",
                        column: x => x.inventory_item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_avatar_attachments_avatar_id_equipped",
                schema: "identity",
                table: "avatar_attachments",
                columns: new[] { "avatar_id", "equipped" });

            migrationBuilder.CreateIndex(
                name: "IX_avatar_attachments_avatar_id_inventory_item_id",
                schema: "identity",
                table: "avatar_attachments",
                columns: new[] { "avatar_id", "inventory_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_avatar_attachments_inventory_item_id",
                schema: "identity",
                table: "avatar_attachments",
                column: "inventory_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avatar_attachments",
                schema: "identity");
        }
    }
}

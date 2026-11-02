using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase05BuildObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "build_operations",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_build_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_build_operations_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "objects",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primitive_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    position_x = table.Column<float>(type: "real", nullable: false),
                    position_y = table.Column<float>(type: "real", nullable: false),
                    position_z = table.Column<float>(type: "real", nullable: false),
                    rotation_x = table.Column<float>(type: "real", nullable: false),
                    rotation_y = table.Column<float>(type: "real", nullable: false),
                    rotation_z = table.Column<float>(type: "real", nullable: false),
                    scale_x = table.Column<float>(type: "real", nullable: false),
                    scale_y = table.Column<float>(type: "real", nullable: false),
                    scale_z = table.Column<float>(type: "real", nullable: false),
                    material_json = table.Column<string>(type: "jsonb", nullable: false),
                    physics_json = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_objects", x => x.id);
                    table.ForeignKey(
                        name: "FK_objects_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_objects_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_build_operations_island_id_created_at",
                schema: "world",
                table: "build_operations",
                columns: new[] { "island_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_objects_group_id",
                schema: "world",
                table: "objects",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_objects_island_id_deleted_at",
                schema: "world",
                table: "objects",
                columns: new[] { "island_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_objects_island_id_owner_user_id",
                schema: "world",
                table: "objects",
                columns: new[] { "island_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_objects_owner_user_id",
                schema: "world",
                table: "objects",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "build_operations",
                schema: "world");

            migrationBuilder.DropTable(
                name: "objects",
                schema: "world");
        }
    }
}

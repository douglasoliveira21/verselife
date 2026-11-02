using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase04PersistentIslands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "world",
                table: "islands",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "social");

            migrationBuilder.AddColumn<string>(
                name: "cover_image_url",
                schema: "world",
                table: "islands",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "world",
                table: "islands",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "environment_json",
                schema: "world",
                table: "islands",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "max_players",
                schema: "world",
                table: "islands",
                type: "integer",
                nullable: false,
                defaultValue: 64);

            migrationBuilder.AddColumn<string>(
                name: "resource_limits_json",
                schema: "world",
                table: "islands",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "runtime_state",
                schema: "world",
                table: "islands",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "suspended");

            migrationBuilder.AddColumn<int>(
                name: "size",
                schema: "world",
                table: "islands",
                type: "integer",
                nullable: false,
                defaultValue: 256);

            migrationBuilder.AddColumn<long>(
                name: "snapshot_version",
                schema: "world",
                table: "islands",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "spawn_x",
                schema: "world",
                table: "islands",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "spawn_y",
                schema: "world",
                table: "islands",
                type: "real",
                nullable: false,
                defaultValue: 1f);

            migrationBuilder.AddColumn<float>(
                name: "spawn_z",
                schema: "world",
                table: "islands",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.CreateTable(
                name: "changes",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    integrity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_changes_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    permission = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    principal_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_permissions_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_permissions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "snapshots",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    terrain_json = table.Column<string>(type: "jsonb", nullable: false),
                    environment_json = table.Column<string>(type: "jsonb", nullable: false),
                    integrity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_snapshots_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_islands_is_public_category_deleted_at",
                schema: "world",
                table: "islands",
                columns: new[] { "is_public", "category", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_changes_island_id_version",
                schema: "world",
                table: "changes",
                columns: new[] { "island_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permissions_island_id_group_id_permission",
                schema: "world",
                table: "permissions",
                columns: new[] { "island_id", "group_id", "permission" },
                unique: true,
                filter: "revoked_at IS NULL AND group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_island_id_user_id_permission",
                schema: "world",
                table: "permissions",
                columns: new[] { "island_id", "user_id", "permission" },
                unique: true,
                filter: "revoked_at IS NULL AND user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_user_id",
                schema: "world",
                table: "permissions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_snapshots_island_id_version",
                schema: "world",
                table: "snapshots",
                columns: new[] { "island_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "changes",
                schema: "world");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "world");

            migrationBuilder.DropTable(
                name: "snapshots",
                schema: "world");

            migrationBuilder.DropIndex(
                name: "IX_islands_is_public_category_deleted_at",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "cover_image_url",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "environment_json",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "max_players",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "resource_limits_json",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "runtime_state",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "size",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "snapshot_version",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "spawn_x",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "spawn_y",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "spawn_z",
                schema: "world",
                table: "islands");
        }
    }
}

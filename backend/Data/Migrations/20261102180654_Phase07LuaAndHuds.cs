using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase07LuaAndHuds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hud_definitions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    layout_json = table.Column<string>(type: "jsonb", nullable: false),
                    script_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hud_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_hud_definitions_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "object_scripts",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    instruction_budget = table.Column<int>(type: "integer", nullable: false),
                    memory_limit_kb = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_scripts", x => x.id);
                    table.ForeignKey(
                        name: "FK_object_scripts_objects_object_id",
                        column: x => x.object_id,
                        principalSchema: "world",
                        principalTable: "objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_object_scripts_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "script_executions",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    script_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    instruction_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_executions", x => x.id);
                    table.ForeignKey(
                        name: "FK_script_executions_object_scripts_script_id",
                        column: x => x.script_id,
                        principalSchema: "world",
                        principalTable: "object_scripts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "script_state",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    script_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_state", x => x.id);
                    table.ForeignKey(
                        name: "FK_script_state_object_scripts_script_id",
                        column: x => x.script_id,
                        principalSchema: "world",
                        principalTable: "object_scripts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hud_definitions_owner_user_id_deleted_at",
                schema: "inventory",
                table: "hud_definitions",
                columns: new[] { "owner_user_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_object_scripts_object_id_version",
                schema: "world",
                table: "object_scripts",
                columns: new[] { "object_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_object_scripts_owner_user_id",
                schema: "world",
                table: "object_scripts",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_script_executions_script_id_created_at",
                schema: "world",
                table: "script_executions",
                columns: new[] { "script_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_script_state_script_id_key",
                schema: "world",
                table: "script_state",
                columns: new[] { "script_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hud_definitions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "script_executions",
                schema: "world");

            migrationBuilder.DropTable(
                name: "script_state",
                schema: "world");

            migrationBuilder.DropTable(
                name: "object_scripts",
                schema: "world");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase87LuaRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "script_grants",
                schema: "world",
                columns: table => new
                {
                    script_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_grants", x => new { x.script_id, x.subject_id, x.permission });
                    table.ForeignKey(
                        name: "FK_script_grants_object_scripts_script_id",
                        column: x => x.script_id,
                        principalSchema: "world",
                        principalTable: "object_scripts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "script_logs",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    script_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    event_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    stack_trace = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    milliseconds = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_script_logs_object_scripts_script_id",
                        column: x => x.script_id,
                        principalSchema: "world",
                        principalTable: "object_scripts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "script_runtime",
                schema: "world",
                columns: table => new
                {
                    script_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    state_text = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    events = table.Column<long>(type: "bigint", nullable: false),
                    events_dropped = table.Column<long>(type: "bigint", nullable: false),
                    errors = table.Column<long>(type: "bigint", nullable: false),
                    violations = table.Column<long>(type: "bigint", nullable: false),
                    suspensions = table.Column<long>(type: "bigint", nullable: false),
                    api_calls = table.Column<long>(type: "bigint", nullable: false),
                    instructions = table.Column<long>(type: "bigint", nullable: false),
                    allocated_bytes = table.Column<long>(type: "bigint", nullable: false),
                    messages_sent = table.Column<long>(type: "bigint", nullable: false),
                    cpu_milliseconds = table.Column<double>(type: "double precision", nullable: false),
                    worst_event_milliseconds = table.Column<double>(type: "double precision", nullable: false),
                    active_timers = table.Column<int>(type: "integer", nullable: false),
                    state_keys = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                    last_error_line = table.Column<int>(type: "integer", nullable: false),
                    suspended_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_runtime", x => x.script_id);
                    table.ForeignKey(
                        name: "FK_script_runtime_object_scripts_script_id",
                        column: x => x.script_id,
                        principalSchema: "world",
                        principalTable: "object_scripts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_script_logs_script_id_created_at",
                schema: "world",
                table: "script_logs",
                columns: new[] { "script_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "script_grants",
                schema: "world");

            migrationBuilder.DropTable(
                name: "script_logs",
                schema: "world");

            migrationBuilder.DropTable(
                name: "script_runtime",
                schema: "world");
        }
    }
}

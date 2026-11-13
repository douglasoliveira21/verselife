using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase89ScriptGrantRevocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "script_grant_revocations",
                schema: "world",
                columns: table => new
                {
                    script_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_grant_revocations", x => new { x.script_id, x.subject_id, x.permission });
                    table.ForeignKey(
                        name: "FK_script_grant_revocations_object_scripts_script_id",
                        column: x => x.script_id,
                        principalSchema: "world",
                        principalTable: "object_scripts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "script_grant_revocations",
                schema: "world");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase51SearchReportsAndMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "search_reports",
                schema: "discovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_search_reports_users_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_stats",
                schema: "discovery",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    query = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    searches = table.Column<int>(type: "integer", nullable: false),
                    zero_results = table.Column<int>(type: "integer", nullable: false),
                    opens = table.Column<int>(type: "integer", nullable: false),
                    teleports = table.Column<int>(type: "integer", nullable: false),
                    total_ms = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_stats", x => new { x.day, x.query });
                });

            migrationBuilder.CreateIndex(
                name: "IX_search_reports_kind_ref_id_status",
                schema: "discovery",
                table: "search_reports",
                columns: new[] { "kind", "ref_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_search_reports_reporter_user_id_created_at",
                schema: "discovery",
                table: "search_reports",
                columns: new[] { "reporter_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_search_reports_reporter_user_id_kind_ref_id",
                schema: "discovery",
                table: "search_reports",
                columns: new[] { "reporter_user_id", "kind", "ref_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "search_reports",
                schema: "discovery");

            migrationBuilder.DropTable(
                name: "search_stats",
                schema: "discovery");
        }
    }
}

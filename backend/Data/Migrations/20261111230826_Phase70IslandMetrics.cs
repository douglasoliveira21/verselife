using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase70IslandMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "island_metrics",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fps = table.Column<double>(type: "double precision", nullable: false),
                    process_ms = table.Column<double>(type: "double precision", nullable: false),
                    memory_mb = table.Column<double>(type: "double precision", nullable: false),
                    tick_rate = table.Column<double>(type: "double precision", nullable: false),
                    players = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_island_metrics", x => x.id);
                    table.ForeignKey(
                        name: "FK_island_metrics_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_island_metrics_island_id_at",
                schema: "world",
                table: "island_metrics",
                columns: new[] { "island_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "island_metrics",
                schema: "world");
        }
    }
}

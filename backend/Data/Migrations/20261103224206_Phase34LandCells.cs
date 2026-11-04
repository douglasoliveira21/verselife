using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase34LandCells : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_cells",
                schema: "world",
                columns: table => new
                {
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cx = table.Column<int>(type: "integer", nullable: false),
                    cz = table.Column<int>(type: "integer", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcel_cells", x => new { x.island_id, x.cx, x.cz });
                    table.ForeignKey(
                        name: "FK_parcel_cells_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_parcel_cells_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "world",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_cells_parcel_id",
                schema: "world",
                table: "parcel_cells",
                column: "parcel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_cells",
                schema: "world");
        }
    }
}

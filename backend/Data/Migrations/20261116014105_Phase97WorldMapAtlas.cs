using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase97WorldMapAtlas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "atlas_x",
                schema: "world",
                table: "islands",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "atlas_y",
                schema: "world",
                table: "islands",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "map_tiles",
                schema: "discovery",
                columns: table => new
                {
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_version = table.Column<long>(type: "bigint", nullable: false),
                    objects_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size = table.Column<int>(type: "integer", nullable: false),
                    png = table.Column<byte[]>(type: "bytea", nullable: false),
                    etag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_map_tiles", x => x.island_id);
                    table.ForeignKey(
                        name: "FK_map_tiles_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_islands_atlas_x_atlas_y",
                schema: "world",
                table: "islands",
                columns: new[] { "atlas_x", "atlas_y" },
                unique: true,
                filter: "atlas_x IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "map_tiles",
                schema: "discovery");

            migrationBuilder.DropIndex(
                name: "IX_islands_atlas_x_atlas_y",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "atlas_x",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "atlas_y",
                schema: "world",
                table: "islands");
        }
    }
}

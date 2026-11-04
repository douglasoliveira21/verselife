using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase48WelcomeAndLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_welcome",
                schema: "world",
                table: "islands",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "player_locations",
                schema: "world",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    x = table.Column<float>(type: "real", nullable: false),
                    y = table.Column<float>(type: "real", nullable: false),
                    z = table.Column<float>(type: "real", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_locations", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_player_locations_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_player_locations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_islands_is_welcome",
                schema: "world",
                table: "islands",
                column: "is_welcome",
                unique: true,
                filter: "is_welcome = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_player_locations_island_id",
                schema: "world",
                table: "player_locations",
                column: "island_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_locations",
                schema: "world");

            migrationBuilder.DropIndex(
                name: "IX_islands_is_welcome",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "is_welcome",
                schema: "world",
                table: "islands");
        }
    }
}

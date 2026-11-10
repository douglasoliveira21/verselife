using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase61TeleportTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "player_count",
                schema: "platform",
                table: "server_instances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "teleport_transfers",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_island_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_x = table.Column<float>(type: "real", nullable: true),
                    target_z = table.Column<float>(type: "real", nullable: true),
                    token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    arrived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teleport_transfers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_teleport_transfers_destination_island_id_status",
                schema: "world",
                table: "teleport_transfers",
                columns: new[] { "destination_island_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_teleport_transfers_source_island_id",
                schema: "world",
                table: "teleport_transfers",
                column: "source_island_id");

            migrationBuilder.CreateIndex(
                name: "IX_teleport_transfers_user_id_idempotency_key",
                schema: "world",
                table: "teleport_transfers",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_teleport_transfers_one_active",
                schema: "world",
                table: "teleport_transfers",
                column: "user_id",
                unique: true,
                filter: "status IN ('prepared', 'connecting')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teleport_transfers",
                schema: "world");

            migrationBuilder.DropColumn(
                name: "player_count",
                schema: "platform",
                table: "server_instances");
        }
    }
}

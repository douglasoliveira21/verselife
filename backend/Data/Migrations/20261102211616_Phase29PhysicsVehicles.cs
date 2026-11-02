using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase29PhysicsVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicles",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    position_x = table.Column<float>(type: "real", nullable: false),
                    position_y = table.Column<float>(type: "real", nullable: false),
                    position_z = table.Column<float>(type: "real", nullable: false),
                    rotation_y = table.Column<float>(type: "real", nullable: false),
                    speed = table.Column<float>(type: "real", nullable: false),
                    max_speed = table.Column<float>(type: "real", nullable: false),
                    mass = table.Column<float>(type: "real", nullable: false),
                    engine_on = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicles", x => x.id);
                    table.ForeignKey(
                        name: "FK_vehicles_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_vehicles_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_seats",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    offset_x = table.Column<float>(type: "real", nullable: false),
                    offset_y = table.Column<float>(type: "real", nullable: false),
                    offset_z = table.Column<float>(type: "real", nullable: false),
                    occupant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occupied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_seats", x => x.id);
                    table.ForeignKey(
                        name: "FK_vehicle_seats_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "world",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_seats_occupant_user_id",
                schema: "world",
                table: "vehicle_seats",
                column: "occupant_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_seats_vehicle_id_seat_type",
                schema: "world",
                table: "vehicle_seats",
                columns: new[] { "vehicle_id", "seat_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_island_id_deleted_at",
                schema: "world",
                table: "vehicles",
                columns: new[] { "island_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_owner_user_id",
                schema: "world",
                table: "vehicles",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_seats",
                schema: "world");

            migrationBuilder.DropTable(
                name: "vehicles",
                schema: "world");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase23Parcels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcels",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    bounds_json = table.Column<string>(type: "jsonb", nullable: false),
                    area = table.Column<double>(type: "double precision", nullable: false),
                    max_objects = table.Column<int>(type: "integer", nullable: false),
                    permissions_json = table.Column<string>(type: "jsonb", nullable: false),
                    environment_json = table.Column<string>(type: "jsonb", nullable: false),
                    spawn_x = table.Column<float>(type: "real", nullable: false),
                    spawn_y = table.Column<float>(type: "real", nullable: false),
                    spawn_z = table.Column<float>(type: "real", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcels", x => x.id);
                    table.ForeignKey(
                        name: "FK_parcels_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_parcels_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_changes",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcel_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_parcel_changes_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "world",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_parcel_changes_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_changes_actor_user_id",
                schema: "world",
                table: "parcel_changes",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_parcel_changes_parcel_id_created_at",
                schema: "world",
                table: "parcel_changes",
                columns: new[] { "parcel_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_parcels_island_id_deleted_at",
                schema: "world",
                table: "parcels",
                columns: new[] { "island_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_parcels_island_id_name",
                schema: "world",
                table: "parcels",
                columns: new[] { "island_id", "name" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_parcels_owner_user_id",
                schema: "world",
                table: "parcels",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_changes",
                schema: "world");

            migrationBuilder.DropTable(
                name: "parcels",
                schema: "world");
        }
    }
}

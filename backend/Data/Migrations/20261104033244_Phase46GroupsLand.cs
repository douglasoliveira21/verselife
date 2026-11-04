using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase46GroupsLand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_group_access",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcel_group_access", x => x.id);
                    table.ForeignKey(
                        name: "FK_parcel_group_access_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "world",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_group_access_group_id",
                schema: "world",
                table: "parcel_group_access",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_parcel_group_access_parcel_id_group_id_kind",
                schema: "world",
                table: "parcel_group_access",
                columns: new[] { "parcel_id", "group_id", "kind" });

            // groups that already exist: administrators may run and build on the land of the group, the leader may do everything
            migrationBuilder.Sql("UPDATE social.group_roles SET permissions = permissions || CASE WHEN permissions = '' THEN '' ELSE ',' END || 'manage_land,build_on_land' WHERE system_key = 'admin' AND permissions NOT LIKE '%manage_land%';");
            migrationBuilder.Sql("UPDATE social.group_roles SET permissions = permissions || ',manage_land,build_on_land,release_land' WHERE system_key = 'owner' AND permissions NOT LIKE '%manage_land%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_group_access",
                schema: "world");
        }
    }
}

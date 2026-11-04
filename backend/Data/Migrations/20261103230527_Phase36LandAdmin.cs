using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase36LandAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "access_mode",
                schema: "world",
                table: "parcels",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "public");

            migrationBuilder.CreateTable(
                name: "parcel_access",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcel_access", x => x.id);
                    table.ForeignKey(
                        name: "FK_parcel_access_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "world",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_access_parcel_id_kind",
                schema: "world",
                table: "parcel_access",
                columns: new[] { "parcel_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_access_user_id",
                schema: "world",
                table: "parcel_access",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_access",
                schema: "world");

            migrationBuilder.DropColumn(
                name: "access_mode",
                schema: "world",
                table: "parcels");
        }
    }
}

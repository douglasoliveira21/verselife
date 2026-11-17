using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase103ScriptPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "script_payout_authorizations",
                schema: "economy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    script_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_per_payment = table.Column<long>(type: "bigint", nullable: false),
                    max_per_day = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_payout_authorizations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_script_payout_authorizations_owner_user_id",
                schema: "economy",
                table: "script_payout_authorizations",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_script_payout_authorizations_script_id_revoked_at",
                schema: "economy",
                table: "script_payout_authorizations",
                columns: new[] { "script_id", "revoked_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "script_payout_authorizations",
                schema: "economy");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase53ItemOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_offers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    keep_copy = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_offers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_item_offers_expires_at",
                schema: "inventory",
                table: "item_offers",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_item_offers_from_user_id_idempotency_key",
                schema: "inventory",
                table: "item_offers",
                columns: new[] { "from_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_offers_from_user_id_status",
                schema: "inventory",
                table: "item_offers",
                columns: new[] { "from_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_item_offers_item_id",
                schema: "inventory",
                table: "item_offers",
                column: "item_id",
                unique: true,
                filter: "status = 'pending' AND keep_copy = false");

            migrationBuilder.CreateIndex(
                name: "IX_item_offers_to_user_id_status",
                schema: "inventory",
                table: "item_offers",
                columns: new[] { "to_user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_offers",
                schema: "inventory");
        }
    }
}

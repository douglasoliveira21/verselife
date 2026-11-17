using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase100CoinOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coin_orders",
                schema: "economy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    coins = table.Column<long>(type: "bigint", nullable: false),
                    price_cents = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    provider = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    provider_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coin_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coin_orders_provider_provider_ref",
                schema: "economy",
                table: "coin_orders",
                columns: new[] { "provider", "provider_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coin_orders_user_id_created_at",
                schema: "economy",
                table: "coin_orders",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_coin_orders_user_id_idempotency_key",
                schema: "economy",
                table: "coin_orders",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coin_orders",
                schema: "economy");
        }
    }
}

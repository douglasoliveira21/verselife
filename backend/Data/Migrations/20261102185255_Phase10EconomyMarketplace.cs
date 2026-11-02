using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10EconomyMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "economy");

            migrationBuilder.EnsureSchema(
                name: "marketplace");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "economy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    category = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    price_coins = table.Column<long>(type: "bigint", nullable: false),
                    permissions_json = table.Column<string>(type: "jsonb", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_products_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "inventory",
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_products_users_seller_user_id",
                        column: x => x.seller_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rental_plans",
                schema: "economy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    price_coins = table.Column<long>(type: "bigint", nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    max_size = table.Column<int>(type: "integer", nullable: false),
                    max_objects = table.Column<int>(type: "integer", nullable: false),
                    max_players = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rental_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "economy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    initiator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "economy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    available_coins = table.Column<long>(type: "bigint", nullable: false),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallets", x => x.id);
                    table.ForeignKey(
                        name: "FK_wallets_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_coins = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                    table.ForeignKey(
                        name: "FK_orders_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "marketplace",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rentals",
                schema: "economy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recovery_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rentals", x => x.id);
                    table.ForeignKey(
                        name: "FK_rentals_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rentals_rental_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "economy",
                        principalTable: "rental_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rentals_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "economy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debit = table.Column<long>(type: "bigint", nullable: false),
                    credit = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_ledger_entries_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "economy",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ledger_entries_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalSchema: "economy",
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_user_id_account_type",
                schema: "economy",
                table: "accounts",
                columns: new[] { "user_id", "account_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_account_id",
                schema: "economy",
                table: "ledger_entries",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_transaction_id",
                schema: "economy",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_buyer_user_id_created_at",
                schema: "marketplace",
                table: "orders",
                columns: new[] { "buyer_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_idempotency_key",
                schema: "marketplace",
                table: "orders",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_product_id",
                schema: "marketplace",
                table: "orders",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_asset_id",
                schema: "marketplace",
                table: "products",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_seller_user_id",
                schema: "marketplace",
                table: "products",
                column: "seller_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_status_category_updated_at",
                schema: "marketplace",
                table: "products",
                columns: new[] { "status", "category", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_rental_plans_is_active",
                schema: "economy",
                table: "rental_plans",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_rentals_island_id_status",
                schema: "economy",
                table: "rentals",
                columns: new[] { "island_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_rentals_plan_id",
                schema: "economy",
                table: "rentals",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_rentals_user_id",
                schema: "economy",
                table: "rentals",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_idempotency_key",
                schema: "economy",
                table: "transactions",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_initiator_user_id_created_at",
                schema: "economy",
                table: "transactions",
                columns: new[] { "initiator_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wallets_user_id",
                schema: "economy",
                table: "wallets",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "rentals",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "products",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "rental_plans",
                schema: "economy");
        }
    }
}

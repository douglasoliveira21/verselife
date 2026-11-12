using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase77IslandSalesAndLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "rent_deposit",
                schema: "world",
                table: "islands",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "rent_grace_days",
                schema: "world",
                table: "islands",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rent_max_periods",
                schema: "world",
                table: "islands",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rent_min_periods",
                schema: "world",
                table: "islands",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rent_period_days",
                schema: "world",
                table: "islands",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "rent_price",
                schema: "world",
                table: "islands",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sale_buyer_user_id",
                schema: "world",
                table: "islands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sale_description",
                schema: "world",
                table: "islands",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "sale_includes_objects",
                schema: "world",
                table: "islands",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "sale_price",
                schema: "world",
                table: "islands",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "island_leases",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    landlord_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_per_period = table.Column<long>(type: "bigint", nullable: false),
                    period_days = table.Column<int>(type: "integer", nullable: false),
                    deposit_coins = table.Column<long>(type: "bigint", nullable: false),
                    grace_days = table.Column<int>(type: "integer", nullable: false),
                    periods = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    end_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    grace_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_island_leases", x => x.id);
                    table.ForeignKey(
                        name: "FK_island_leases_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "island_transactions",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_island_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_island_transactions_islands_island_id",
                        column: x => x.island_id,
                        principalSchema: "world",
                        principalTable: "islands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_island_leases_island_id_status",
                schema: "world",
                table: "island_leases",
                columns: new[] { "island_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_island_leases_tenant_user_id_status",
                schema: "world",
                table: "island_leases",
                columns: new[] { "tenant_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_island_transactions_island_id_created_at",
                schema: "world",
                table: "island_transactions",
                columns: new[] { "island_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "island_leases",
                schema: "world");

            migrationBuilder.DropTable(
                name: "island_transactions",
                schema: "world");

            migrationBuilder.DropColumn(
                name: "rent_deposit",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "rent_grace_days",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "rent_max_periods",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "rent_min_periods",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "rent_period_days",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "rent_price",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "sale_buyer_user_id",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "sale_description",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "sale_includes_objects",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "sale_price",
                schema: "world",
                table: "islands");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase35LandCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "rent_auto_renew_allowed",
                schema: "world",
                table: "parcels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "rent_deposit",
                schema: "world",
                table: "parcels",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "rent_grace_days",
                schema: "world",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rent_max_periods",
                schema: "world",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rent_min_periods",
                schema: "world",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rent_period_days",
                schema: "world",
                table: "parcels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "rent_price",
                schema: "world",
                table: "parcels",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sale_buyer_user_id",
                schema: "world",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sale_description",
                schema: "world",
                table: "parcels",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "sale_includes_objects",
                schema: "world",
                table: "parcels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "sale_price",
                schema: "world",
                table: "parcels",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "parcel_rentals",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    landlord_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_per_period = table.Column<long>(type: "bigint", nullable: false),
                    period_days = table.Column<int>(type: "integer", nullable: false),
                    deposit_coins = table.Column<long>(type: "bigint", nullable: false),
                    grace_days = table.Column<int>(type: "integer", nullable: false),
                    periods = table.Column<int>(type: "integer", nullable: false),
                    auto_renew = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    end_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    grace_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcel_rentals", x => x.id);
                    table.ForeignKey(
                        name: "FK_parcel_rentals_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "world",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parcel_transactions",
                schema: "world",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_parcel_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_parcel_transactions_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "world",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_rentals_parcel_id_status",
                schema: "world",
                table: "parcel_rentals",
                columns: new[] { "parcel_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_rentals_tenant_user_id_status",
                schema: "world",
                table: "parcel_rentals",
                columns: new[] { "tenant_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_transactions_parcel_id_created_at",
                schema: "world",
                table: "parcel_transactions",
                columns: new[] { "parcel_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_rentals",
                schema: "world");

            migrationBuilder.DropTable(
                name: "parcel_transactions",
                schema: "world");

            migrationBuilder.DropColumn(
                name: "rent_auto_renew_allowed",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "rent_deposit",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "rent_grace_days",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "rent_max_periods",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "rent_min_periods",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "rent_period_days",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "rent_price",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "sale_buyer_user_id",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "sale_description",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "sale_includes_objects",
                schema: "world",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "sale_price",
                schema: "world",
                table: "parcels");
        }
    }
}

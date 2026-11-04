using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase54MarketplaceRights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_gift",
                schema: "marketplace",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "buyer_next_rights",
                schema: "marketplace",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "buyer_rights",
                schema: "marketplace",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<Guid>(
                name: "source_item_id",
                schema: "marketplace",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recipient_user_id",
                schema: "marketplace",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_source_item_id",
                schema: "marketplace",
                table: "products",
                column: "source_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_source_item_id",
                schema: "marketplace",
                table: "products");

            migrationBuilder.DropColumn(
                name: "allow_gift",
                schema: "marketplace",
                table: "products");

            migrationBuilder.DropColumn(
                name: "buyer_next_rights",
                schema: "marketplace",
                table: "products");

            migrationBuilder.DropColumn(
                name: "buyer_rights",
                schema: "marketplace",
                table: "products");

            migrationBuilder.DropColumn(
                name: "source_item_id",
                schema: "marketplace",
                table: "products");

            migrationBuilder.DropColumn(
                name: "recipient_user_id",
                schema: "marketplace",
                table: "orders");
        }
    }
}

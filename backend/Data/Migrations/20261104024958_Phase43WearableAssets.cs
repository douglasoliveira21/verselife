using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase43WearableAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "asset_id",
                schema: "identity",
                table: "avatar_wearables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_avatar_wearables_asset_id",
                schema: "identity",
                table: "avatar_wearables",
                column: "asset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_avatar_wearables_asset_id",
                schema: "identity",
                table: "avatar_wearables");

            migrationBuilder.DropColumn(
                name: "asset_id",
                schema: "identity",
                table: "avatar_wearables");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase82IslandTransferOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transfer_expires_at",
                schema: "world",
                table: "islands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_to_user_id",
                schema: "world",
                table: "islands",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "transfer_expires_at",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "transfer_to_user_id",
                schema: "world",
                table: "islands");
        }
    }
}

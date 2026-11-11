using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase75TerrainBackups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "backup_at",
                schema: "world",
                table: "snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "backup_by_user_id",
                schema: "world",
                table: "snapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "backup_name",
                schema: "world",
                table: "snapshots",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "restore_snapshot_id",
                schema: "world",
                table: "island_restarts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "backup_at",
                schema: "world",
                table: "snapshots");

            migrationBuilder.DropColumn(
                name: "backup_by_user_id",
                schema: "world",
                table: "snapshots");

            migrationBuilder.DropColumn(
                name: "backup_name",
                schema: "world",
                table: "snapshots");

            migrationBuilder.DropColumn(
                name: "restore_snapshot_id",
                schema: "world",
                table: "island_restarts");
        }
    }
}

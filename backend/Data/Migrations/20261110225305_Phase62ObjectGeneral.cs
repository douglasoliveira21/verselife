using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase62ObjectGeneral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "click_action",
                schema: "world",
                table: "objects",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "touch");

            migrationBuilder.AddColumn<Guid>(
                name: "last_owner_user_id",
                schema: "world",
                table: "objects",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "click_action",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "last_owner_user_id",
                schema: "world",
                table: "objects");
        }
    }
}

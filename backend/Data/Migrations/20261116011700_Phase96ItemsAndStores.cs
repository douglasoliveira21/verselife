using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase96ItemsAndStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "creator",
                schema: "discovery",
                table: "search_index",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "rights",
                schema: "discovery",
                table: "search_index",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "storefronts",
                schema: "discovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    category = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    logo_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    banner_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storefronts", x => x.id);
                    table.ForeignKey(
                        name: "FK_storefronts_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_storefronts_owner_user_id_deleted_at",
                schema: "discovery",
                table: "storefronts",
                columns: new[] { "owner_user_id", "deleted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storefronts",
                schema: "discovery");

            migrationBuilder.DropColumn(
                name: "creator",
                schema: "discovery",
                table: "search_index");

            migrationBuilder.DropColumn(
                name: "rights",
                schema: "discovery",
                table: "search_index");
        }
    }
}

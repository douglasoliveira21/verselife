using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase52ItemRights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "copied_from_item_id",
                schema: "inventory",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                schema: "inventory",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "group_rights",
                schema: "inventory",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "next_owner_rights",
                schema: "inventory",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "owner_rights",
                schema: "inventory",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                schema: "inventory",
                table: "items",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "show_owner_history",
                schema: "inventory",
                table: "items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "inventory",
                table: "items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.CreateTable(
                name: "item_audit",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    outcome = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_owners",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_owners", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_items_group_id",
                schema: "inventory",
                table: "items",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_item_audit_actor_user_id_at",
                schema: "inventory",
                table: "item_audit",
                columns: new[] { "actor_user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_item_audit_at",
                schema: "inventory",
                table: "item_audit",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "IX_item_audit_item_id_at",
                schema: "inventory",
                table: "item_audit",
                columns: new[] { "item_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_item_owners_item_id_at",
                schema: "inventory",
                table: "item_owners",
                columns: new[] { "item_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_item_owners_user_id_at",
                schema: "inventory",
                table: "item_owners",
                columns: new[] { "user_id", "at" });

            // Items that already exist keep every right (nothing was restricted before), and each one gets the first line of its history: held by its owner since it was made.
            migrationBuilder.Sql(@"INSERT INTO inventory.item_owners (id, item_id, asset_id, user_id, from_user_id, reason, at)
                SELECT gen_random_uuid(), i.id, i.asset_id, i.owner_user_id, NULL, 'legacy', i.created_at FROM inventory.items i;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_audit",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "item_owners",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_items_group_id",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "copied_from_item_id",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "group_id",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "group_rights",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "next_owner_rights",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "owner_rights",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "show_owner_history",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "inventory",
                table: "items");
        }
    }
}

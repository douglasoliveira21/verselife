using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase47GroupsEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                schema: "marketplace",
                table: "world_stores",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                schema: "marketplace",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                schema: "economy",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "group_ledger",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_ledger", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_ledger_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_treasuries",
                schema: "social",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    total_in = table.Column<long>(type: "bigint", nullable: false),
                    total_out = table.Column<long>(type: "bigint", nullable: false),
                    frozen = table.Column<bool>(type: "boolean", nullable: false),
                    frozen_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    frozen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_treasuries", x => x.group_id);
                    table.ForeignKey(
                        name: "FK_group_treasuries_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_group_id",
                schema: "economy",
                table: "accounts",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_ledger_group_id_created_at",
                schema: "social",
                table: "group_ledger",
                columns: new[] { "group_id", "created_at" });

            // groups that already exist: administrators may see the chest and run the shops; the leader holds all
            migrationBuilder.Sql("UPDATE social.group_roles SET permissions = permissions || CASE WHEN permissions = '' THEN '' ELSE ',' END || 'view_treasury,manage_store' WHERE system_key = 'admin' AND permissions NOT LIKE '%view_treasury%';");
            migrationBuilder.Sql("UPDATE social.group_roles SET permissions = permissions || ',view_treasury,manage_treasury,manage_store' WHERE system_key = 'owner' AND permissions NOT LIKE '%view_treasury%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_ledger",
                schema: "social");

            migrationBuilder.DropTable(
                name: "group_treasuries",
                schema: "social");

            migrationBuilder.DropIndex(
                name: "IX_accounts_group_id",
                schema: "economy",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "group_id",
                schema: "marketplace",
                table: "world_stores");

            migrationBuilder.DropColumn(
                name: "group_id",
                schema: "marketplace",
                table: "products");

            migrationBuilder.DropColumn(
                name: "group_id",
                schema: "economy",
                table: "accounts");
        }
    }
}

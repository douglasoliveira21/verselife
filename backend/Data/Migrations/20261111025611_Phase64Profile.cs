using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase64Profile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "account_type",
                schema: "identity",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "standard");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "donor_since",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_life_bio",
                schema: "identity",
                table: "users",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "interests",
                schema: "identity",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "profile_photo_asset_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_in_profile",
                schema: "social",
                table: "group_members",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "marriages",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    married_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marriages", x => x.id);
                    table.ForeignKey(
                        name: "FK_marriages_users_proposer_user_id",
                        column: x => x.proposer_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_marriages_users_target_user_id",
                        column: x => x.target_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marriages_proposer_user_id_status",
                schema: "social",
                table: "marriages",
                columns: new[] { "proposer_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_marriages_target_user_id_status",
                schema: "social",
                table: "marriages",
                columns: new[] { "target_user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marriages",
                schema: "social");

            migrationBuilder.DropColumn(
                name: "account_type",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "donor_since",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "first_life_bio",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "interests",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "profile_photo_asset_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "show_in_profile",
                schema: "social",
                table: "group_members");
        }
    }
}

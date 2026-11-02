using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase11Administration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "identity",
                table: "users",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.CreateTable(
                name: "admin_audits",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_audits", x => x.id);
                    table.ForeignKey(
                        name: "FK_admin_audits_users_admin_user_id",
                        column: x => x.admin_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_cases",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_cases", x => x.id);
                    table.ForeignKey(
                        name: "FK_moderation_cases_users_reviewer_user_id",
                        column: x => x.reviewer_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_audits_admin_user_id",
                schema: "platform",
                table: "admin_audits",
                column: "admin_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_admin_audits_created_at",
                schema: "platform",
                table: "admin_audits",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_cases_created_at",
                schema: "platform",
                table: "moderation_cases",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_cases_reviewer_user_id",
                schema: "platform",
                table: "moderation_cases",
                column: "reviewer_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audits",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "moderation_cases",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "identity",
                table: "users");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase85GroupsActiveAndProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "title",
                schema: "social",
                table: "group_roles",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "social",
                table: "group_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "group_proposals",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    options_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    min_rank = table.Column<int>(type: "integer", nullable: false),
                    quorum_percent = table.Column<int>(type: "integer", nullable: false),
                    criterion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    winning_option = table.Column<int>(type: "integer", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eligible_at_close = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_proposals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_votes",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_votes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_group_proposals_group_id_status",
                schema: "social",
                table: "group_proposals",
                columns: new[] { "group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_group_proposals_status_ends_at",
                schema: "social",
                table: "group_proposals",
                columns: new[] { "status", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "IX_group_votes_proposal_id_user_id",
                schema: "social",
                table: "group_votes",
                columns: new[] { "proposal_id", "user_id" },
                unique: true);

            // groups that already exist: the leader and the administrator can run votes
            migrationBuilder.Sql("UPDATE social.group_roles SET permissions = permissions || ',manage_proposals' WHERE system_key IN ('admin','owner') AND permissions NOT LIKE '%manage_proposals%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_proposals",
                schema: "social");

            migrationBuilder.DropTable(
                name: "group_votes",
                schema: "social");

            migrationBuilder.DropColumn(
                name: "title",
                schema: "social",
                table: "group_roles");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "social",
                table: "group_members");
        }
    }
}

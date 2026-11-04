using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase45GroupsCommunication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "notifications_muted",
                schema: "social",
                table: "group_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                schema: "discovery",
                table: "events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "group_announcements",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    pinned = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_announcements", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_announcements_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_notifications",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_notifications_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_group_announcements_group_id_created_at",
                schema: "social",
                table: "group_announcements",
                columns: new[] { "group_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_group_notifications_group_id",
                schema: "social",
                table: "group_notifications",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_notifications_user_id_read_at_created_at",
                schema: "social",
                table: "group_notifications",
                columns: new[] { "user_id", "read_at", "created_at" });

            // groups that already exist: their administrators and moderators get the new communication permissions
            migrationBuilder.Sql("UPDATE social.group_roles SET permissions = permissions || CASE WHEN permissions = '' THEN '' ELSE ',' END || 'announce,manage_events,moderate_chat' WHERE system_key = 'admin' AND permissions NOT LIKE '%announce%';");
            migrationBuilder.Sql("UPDATE social.group_roles SET permissions = permissions || CASE WHEN permissions = '' THEN '' ELSE ',' END || 'moderate_chat' WHERE system_key = 'moderator' AND permissions NOT LIKE '%moderate_chat%';");
            migrationBuilder.Sql("UPDATE social.group_roles SET permissions = permissions || ',announce,manage_events,moderate_chat' WHERE system_key = 'owner' AND permissions NOT LIKE '%announce%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_announcements",
                schema: "social");

            migrationBuilder.DropTable(
                name: "group_notifications",
                schema: "social");

            migrationBuilder.DropColumn(
                name: "notifications_muted",
                schema: "social",
                table: "group_members");

            migrationBuilder.DropColumn(
                name: "group_id",
                schema: "discovery",
                table: "events");
        }
    }
}

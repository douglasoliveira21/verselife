using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase44GroupsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accent_color",
                schema: "social",
                table: "groups",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#12F2E0");

            migrationBuilder.AddColumn<string>(
                name: "emblem",
                schema: "social",
                table: "groups",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "people");

            migrationBuilder.AddColumn<string>(
                name: "join_policy",
                schema: "social",
                table: "groups",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "invite");

            migrationBuilder.AddColumn<int>(
                name: "member_limit",
                schema: "social",
                table: "groups",
                type: "integer",
                nullable: false,
                defaultValue: 500);

            migrationBuilder.AddColumn<string>(
                name: "name_key",
                schema: "social",
                table: "groups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "privacy",
                schema: "social",
                table: "groups",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "private");

            migrationBuilder.AddColumn<string>(
                name: "tag",
                schema: "social",
                table: "groups",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "muted_until",
                schema: "social",
                table: "group_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                schema: "social",
                table: "group_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "group_audit",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_audit", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_audit_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_bans",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    banned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_bans", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_bans_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_join_requests",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_join_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_join_requests_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_roles",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    system_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    permissions = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_roles", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_roles_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_groups_name_key",
                schema: "social",
                table: "groups",
                column: "name_key");

            migrationBuilder.CreateIndex(
                name: "IX_groups_tag",
                schema: "social",
                table: "groups",
                column: "tag");

            migrationBuilder.CreateIndex(
                name: "IX_group_members_role_id",
                schema: "social",
                table: "group_members",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_audit_group_id_created_at",
                schema: "social",
                table: "group_audit",
                columns: new[] { "group_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_group_bans_group_id_user_id",
                schema: "social",
                table: "group_bans",
                columns: new[] { "group_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_group_join_requests_group_id_user_id_status",
                schema: "social",
                table: "group_join_requests",
                columns: new[] { "group_id", "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_group_roles_group_id_name",
                schema: "social",
                table: "group_roles",
                columns: new[] { "group_id", "name" },
                unique: true);

            // groups that existed before cargos: give each the four starting roles and put every member in the one the old role names
            migrationBuilder.Sql("UPDATE social.groups SET name_key = lower(name) WHERE name_key = '';");
            var values = string.Join(", ", LifeVerse.Shared.Groups.GroupRules.StartingRoles.Select(r => $"('{r.Key}', '{r.Name}', {r.Rank}, '{LifeVerse.Shared.Groups.GroupRules.JoinPermissions(r.Permissions)}')"));
            migrationBuilder.Sql($"INSERT INTO social.group_roles (id, group_id, name, system_key, rank, permissions, color, created_at) SELECT gen_random_uuid(), g.id, r.name, r.key, r.rank, r.perms, '#A9B3BD', now() FROM social.groups g CROSS JOIN (VALUES {values}) AS r(key, name, rank, perms) WHERE NOT EXISTS (SELECT 1 FROM social.group_roles x WHERE x.group_id = g.id AND x.system_key = r.key);");
            migrationBuilder.Sql("UPDATE social.group_members m SET role_id = (SELECT x.id FROM social.group_roles x WHERE x.group_id = m.group_id AND x.system_key = CASE WHEN m.role IN ('owner', 'admin', 'moderator', 'member') THEN m.role ELSE 'member' END) WHERE m.role_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_audit",
                schema: "social");

            migrationBuilder.DropTable(
                name: "group_bans",
                schema: "social");

            migrationBuilder.DropTable(
                name: "group_join_requests",
                schema: "social");

            migrationBuilder.DropTable(
                name: "group_roles",
                schema: "social");

            migrationBuilder.DropIndex(
                name: "IX_groups_name_key",
                schema: "social",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_groups_tag",
                schema: "social",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_group_members_role_id",
                schema: "social",
                table: "group_members");

            migrationBuilder.DropColumn(
                name: "accent_color",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "emblem",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "join_policy",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "member_limit",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "name_key",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "privacy",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "tag",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "muted_until",
                schema: "social",
                table: "group_members");

            migrationBuilder.DropColumn(
                name: "role_id",
                schema: "social",
                table: "group_members");
        }
    }
}

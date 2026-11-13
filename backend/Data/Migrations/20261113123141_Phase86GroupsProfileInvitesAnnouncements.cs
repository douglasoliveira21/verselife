using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase86GroupsProfileInvitesAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "social",
                table: "groups",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "social");

            migrationBuilder.AddColumn<string>(
                name: "content_rating",
                schema: "social",
                table: "groups",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "livre");

            migrationBuilder.AddColumn<string>(
                name: "cover_url",
                schema: "social",
                table: "groups",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "headquarters_island_id",
                schema: "social",
                table: "groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "join_fee",
                schema: "social",
                table: "groups",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "language",
                schema: "social",
                table: "groups",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "pt-BR");

            migrationBuilder.AddColumn<string>(
                name: "message",
                schema: "social",
                table: "group_invites",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                schema: "social",
                table: "group_invites",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_url",
                schema: "social",
                table: "group_announcements",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "social",
                table: "group_announcements",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "content_rating",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "cover_url",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "headquarters_island_id",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "join_fee",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "language",
                schema: "social",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "message",
                schema: "social",
                table: "group_invites");

            migrationBuilder.DropColumn(
                name: "role_id",
                schema: "social",
                table: "group_invites");

            migrationBuilder.DropColumn(
                name: "attachment_url",
                schema: "social",
                table: "group_announcements");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "social",
                table: "group_announcements");
        }
    }
}

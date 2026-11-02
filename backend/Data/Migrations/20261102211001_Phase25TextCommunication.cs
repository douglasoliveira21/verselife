using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase25TextCommunication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_sender_user_id",
                schema: "social",
                table: "messages");

            migrationBuilder.AddColumn<string>(
                name: "presence_status",
                schema: "identity",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "presence_updated_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "privacy_json",
                schema: "identity",
                table: "users",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "client_message_id",
                schema: "social",
                table: "messages",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "read_at",
                schema: "social",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_presence_status",
                schema: "identity",
                table: "users",
                column: "presence_status");

            migrationBuilder.CreateIndex(
                name: "IX_messages_sender_user_id_client_message_id",
                schema: "social",
                table: "messages",
                columns: new[] { "sender_user_id", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_presence_status",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_messages_sender_user_id_client_message_id",
                schema: "social",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "presence_status",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "presence_updated_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "privacy_json",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "client_message_id",
                schema: "social",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "read_at",
                schema: "social",
                table: "messages");

            migrationBuilder.CreateIndex(
                name: "IX_messages_sender_user_id",
                schema: "social",
                table: "messages",
                column: "sender_user_id");
        }
    }
}

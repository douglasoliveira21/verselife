using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase18AdvancedObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_json",
                schema: "world",
                table: "objects",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "creator_user_id",
                schema: "world",
                table: "objects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "world",
                table: "objects",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "world",
                table: "objects",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "object");

            migrationBuilder.AddColumn<string>(
                name: "owner_history_json",
                schema: "world",
                table: "objects",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_object_id",
                schema: "world",
                table: "objects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "permissions_json",
                schema: "world",
                table: "objects",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"modify\":true,\"copy\":true,\"transfer\":false,\"use\":true,\"move\":true,\"delete\":true,\"return\":true}");

            migrationBuilder.Sql("UPDATE world.objects SET creator_user_id = owner_user_id WHERE creator_user_id IS NULL;");
            migrationBuilder.AlterColumn<Guid>(name: "creator_user_id", schema: "world", table: "objects", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_objects_creator_user_id",
                schema: "world",
                table: "objects",
                column: "creator_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_objects_parent_object_id",
                schema: "world",
                table: "objects",
                column: "parent_object_id");

            migrationBuilder.AddForeignKey(
                name: "FK_objects_objects_parent_object_id",
                schema: "world",
                table: "objects",
                column: "parent_object_id",
                principalSchema: "world",
                principalTable: "objects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_objects_users_creator_user_id",
                schema: "world",
                table: "objects",
                column: "creator_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_objects_objects_parent_object_id",
                schema: "world",
                table: "objects");

            migrationBuilder.DropForeignKey(
                name: "FK_objects_users_creator_user_id",
                schema: "world",
                table: "objects");

            migrationBuilder.DropIndex(
                name: "IX_objects_creator_user_id",
                schema: "world",
                table: "objects");

            migrationBuilder.DropIndex(
                name: "IX_objects_parent_object_id",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "content_json",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "creator_user_id",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "name",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "owner_history_json",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "parent_object_id",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "permissions_json",
                schema: "world",
                table: "objects");
        }
    }
}

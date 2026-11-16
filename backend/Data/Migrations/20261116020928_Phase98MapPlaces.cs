using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase98MapPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "discovery",
                table: "landmarks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                schema: "discovery",
                table: "landmarks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "landmark_folders",
                schema: "discovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landmark_folders", x => x.id);
                    table.ForeignKey(
                        name: "FK_landmark_folders_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_landmark_folders_owner_user_id",
                schema: "discovery",
                table: "landmark_folders",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "landmark_folders",
                schema: "discovery");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "discovery",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "folder_id",
                schema: "discovery",
                table: "landmarks");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase39AvatarLooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "avatar_looks",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    share_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avatar_looks", x => x.id);
                    table.ForeignKey(
                        name: "FK_avatar_looks_avatars_avatar_id",
                        column: x => x.avatar_id,
                        principalSchema: "identity",
                        principalTable: "avatars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_avatar_looks_avatar_id_name",
                schema: "identity",
                table: "avatar_looks",
                columns: new[] { "avatar_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_avatar_looks_share_code",
                schema: "identity",
                table: "avatar_looks",
                column: "share_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avatar_looks",
                schema: "identity");
        }
    }
}

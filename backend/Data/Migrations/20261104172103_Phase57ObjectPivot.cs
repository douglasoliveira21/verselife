using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase57ObjectPivot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "pivot_x",
                schema: "world",
                table: "objects",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "pivot_y",
                schema: "world",
                table: "objects",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "pivot_z",
                schema: "world",
                table: "objects",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pivot_x",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "pivot_y",
                schema: "world",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "pivot_z",
                schema: "world",
                table: "objects");
        }
    }
}

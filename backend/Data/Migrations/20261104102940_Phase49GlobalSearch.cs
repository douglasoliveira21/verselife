using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase49GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "discovery",
                table: "landmarks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateTable(
                name: "search_index",
                schema: "discovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    category = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    search_text = table.Column<string>(type: "text", nullable: false),
                    island_id = table.Column<Guid>(type: "uuid", nullable: true),
                    x = table.Column<float>(type: "real", nullable: true),
                    z = table.Column<float>(type: "real", nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    popularity = table.Column<double>(type: "double precision", nullable: false),
                    hidden = table.Column<bool>(type: "boolean", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_index", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_state",
                schema: "discovery",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ran_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entries = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_state", x => x.kind);
                });

            migrationBuilder.CreateIndex(
                name: "IX_search_index_expires_at",
                schema: "discovery",
                table: "search_index",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_search_index_island_id",
                schema: "discovery",
                table: "search_index",
                column: "island_id");

            migrationBuilder.CreateIndex(
                name: "IX_search_index_kind_popularity",
                schema: "discovery",
                table: "search_index",
                columns: new[] { "kind", "popularity" });

            migrationBuilder.CreateIndex(
                name: "IX_search_index_kind_ref_id",
                schema: "discovery",
                table: "search_index",
                columns: new[] { "kind", "ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_search_index_kind_source_updated_at",
                schema: "discovery",
                table: "search_index",
                columns: new[] { "kind", "source_updated_at" });

            // landmarks had no 'changed at': before today they changed when they were made
            migrationBuilder.Sql("UPDATE discovery.landmarks SET updated_at = COALESCE(deleted_at, created_at) WHERE updated_at < created_at;");

            // full-text search: PostgreSQL builds the vector from the normalized text and keeps it up to date; the GIN index finds words and word beginnings fast
            migrationBuilder.Sql("ALTER TABLE discovery.search_index ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (to_tsvector('simple', search_text)) STORED;");
            migrationBuilder.Sql("CREATE INDEX ix_search_index_search_vector ON discovery.search_index USING GIN (search_vector);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "search_index",
                schema: "discovery");

            migrationBuilder.DropTable(
                name: "search_state",
                schema: "discovery");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "discovery",
                table: "landmarks");
        }
    }
}

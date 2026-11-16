using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeVerse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase95EventsAndPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "price_coins",
                schema: "discovery",
                table: "search_index",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rating",
                schema: "discovery",
                table: "search_index",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "allow_guest_events",
                schema: "world",
                table: "islands",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<float>(
                name: "arrival_x",
                schema: "discovery",
                table: "events",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "arrival_z",
                schema: "discovery",
                table: "events",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "discovery",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "discovery",
                table: "events",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "content_rating",
                schema: "discovery",
                table: "events",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                schema: "discovery",
                table: "events",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "price_coins",
                schema: "discovery",
                table: "events",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "discovery",
                table: "events",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<bool>(
                name: "going",
                schema: "discovery",
                table: "event_attendees",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "in_calendar",
                schema: "discovery",
                table: "event_attendees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // events from before had no change time: it starts as the creation time, so the search index reads them as they were
            migrationBuilder.Sql("UPDATE discovery.events SET updated_at = created_at WHERE updated_at < created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "price_coins",
                schema: "discovery",
                table: "search_index");

            migrationBuilder.DropColumn(
                name: "rating",
                schema: "discovery",
                table: "search_index");

            migrationBuilder.DropColumn(
                name: "allow_guest_events",
                schema: "world",
                table: "islands");

            migrationBuilder.DropColumn(
                name: "arrival_x",
                schema: "discovery",
                table: "events");

            migrationBuilder.DropColumn(
                name: "arrival_z",
                schema: "discovery",
                table: "events");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "discovery",
                table: "events");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "discovery",
                table: "events");

            migrationBuilder.DropColumn(
                name: "content_rating",
                schema: "discovery",
                table: "events");

            migrationBuilder.DropColumn(
                name: "image_url",
                schema: "discovery",
                table: "events");

            migrationBuilder.DropColumn(
                name: "price_coins",
                schema: "discovery",
                table: "events");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "discovery",
                table: "events");

            migrationBuilder.DropColumn(
                name: "going",
                schema: "discovery",
                table: "event_attendees");

            migrationBuilder.DropColumn(
                name: "in_calendar",
                schema: "discovery",
                table: "event_attendees");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableStreamEventLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "stream_event_cursor_seq",
                schema: "activitypub");

            migrationBuilder.CreateTable(
                name: "stream_events",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cursor = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('activitypub.stream_event_cursor_seq')"),
                    deduplication_key = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resource_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_local = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stream_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stream_events_actor_cursor",
                schema: "activitypub",
                table: "stream_events",
                columns: new[] { "actor_iri", "cursor" });

            migrationBuilder.CreateIndex(
                name: "ix_stream_events_kind_cursor",
                schema: "activitypub",
                table: "stream_events",
                columns: new[] { "kind", "cursor" });

            migrationBuilder.CreateIndex(
                name: "ux_stream_events_cursor",
                schema: "activitypub",
                table: "stream_events",
                column: "cursor",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_stream_events_deduplication",
                schema: "activitypub",
                table: "stream_events",
                column: "deduplication_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stream_events",
                schema: "activitypub");

            migrationBuilder.DropSequence(
                name: "stream_event_cursor_seq",
                schema: "activitypub");
        }
    }
}

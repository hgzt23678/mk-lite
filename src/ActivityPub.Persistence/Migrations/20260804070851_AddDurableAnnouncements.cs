using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "announcement_sort_seq",
                schema: "activitypub");

            migrationBuilder.CreateTable(
                name: "announcements",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_ordinal = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('activitypub.announcement_sort_seq')"),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    text = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false),
                    image_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    audience = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "announcement_reads",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    announcement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reader_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcement_reads", x => x.id);
                    table.ForeignKey(
                        name: "FK_announcement_reads_announcements_announcement_id",
                        column: x => x.announcement_id,
                        principalSchema: "activitypub",
                        principalTable: "announcements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcement_reads_actor_created",
                schema: "activitypub",
                table: "announcement_reads",
                columns: new[] { "reader_actor_iri", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_announcement_reads_announcement_actor",
                schema: "activitypub",
                table: "announcement_reads",
                columns: new[] { "announcement_id", "reader_actor_iri" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_announcements_active_audience_order",
                schema: "activitypub",
                table: "announcements",
                columns: new[] { "audience", "sort_ordinal" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_active_publication_window",
                schema: "activitypub",
                table: "announcements",
                columns: new[] { "published_at", "expires_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_announcements_sort_ordinal",
                schema: "activitypub",
                table: "announcements",
                column: "sort_ordinal",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcement_reads",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "announcements",
                schema: "activitypub");

            migrationBuilder.DropSequence(
                name: "announcement_sort_seq",
                schema: "activitypub");
        }
    }
}

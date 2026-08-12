using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteActorMediaCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Creating an empty table is metadata-only, but PostgreSQL still needs short locks on
            // the referenced media and remote_actors tables while installing the foreign keys.
            // Fail the one-shot migration job instead of queueing writes indefinitely; the whole
            // transactional migration can then be retried after the blocking transaction drains.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.Sql("SET LOCAL statement_timeout = '60s';");

            migrationBuilder.CreateTable(
                name: "remote_actor_media_cache",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    remote_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    source_token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remote_etag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    remote_last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    retry_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_actor_media_cache", x => x.id);
                    table.ForeignKey(
                        name: "FK_remote_actor_media_cache_media_media_id",
                        column: x => x.media_id,
                        principalSchema: "activitypub",
                        principalTable: "media",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_remote_actor_media_cache_remote_actors_remote_actor_id",
                        column: x => x.remote_actor_id,
                        principalSchema: "activitypub",
                        principalTable: "remote_actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_remote_actor_media_cache_expiry",
                schema: "activitypub",
                table: "remote_actor_media_cache",
                columns: new[] { "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_remote_actor_media_cache_lease",
                schema: "activitypub",
                table: "remote_actor_media_cache",
                columns: new[] { "lease_expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_remote_actor_media_cache_media_id",
                schema: "activitypub",
                table: "remote_actor_media_cache",
                column: "media_id");

            migrationBuilder.CreateIndex(
                name: "ux_remote_actor_media_cache_actor_token",
                schema: "activitypub",
                table: "remote_actor_media_cache",
                columns: new[] { "remote_actor_id", "source_token" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.DropTable(
                name: "remote_actor_media_cache",
                schema: "activitypub");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticAggregatesDeliveryTargetsMediaRetentionAndMutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "audit_raw_json",
                schema: "activitypub",
                table: "objects",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "raw_json_purged_at",
                schema: "activitypub",
                table: "objects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audit_raw_json",
                schema: "activitypub",
                table: "object_revisions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "raw_json_purged_at",
                schema: "activitypub",
                table: "object_revisions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audit_raw_json",
                schema: "activitypub",
                table: "activities",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "raw_json_purged_at",
                schema: "activitypub",
                table: "activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "actor_moves",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    target_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actor_moves", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "announce_relations",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    object_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announce_relations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collection_memberships",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    collection_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    object_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    add_activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    remove_activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_memberships", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_endpoint_changes",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_endpoint_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    replacement_endpoint_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_endpoint_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_endpoint_changes_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "activitypub",
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_targets",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_targets", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_targets_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "activitypub",
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "legal_holds",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    placed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_holds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "like_relations",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    object_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_like_relations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remote_media_cache",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    source_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    etag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_media_cache", x => x.id);
                    table.ForeignKey(
                        name: "FK_remote_media_cache_media_media_id",
                        column: x => x.media_id,
                        principalSchema: "activitypub",
                        principalTable: "media",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_remote_media_cache_objects_object_id",
                        column: x => x.object_id,
                        principalSchema: "activitypub",
                        principalTable: "objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_mutes",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    target_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    hide_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_mutes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_actor_moves_active_actor",
                schema: "activitypub",
                table: "actor_moves",
                column: "actor_iri",
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_actor_moves_activity",
                schema: "activitypub",
                table: "actor_moves",
                column: "activity_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_announce_relations_active_pair",
                schema: "activitypub",
                table: "announce_relations",
                columns: new[] { "actor_iri", "object_iri" },
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_announce_relations_activity",
                schema: "activitypub",
                table: "announce_relations",
                column: "activity_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_collection_memberships_active_item",
                schema: "activitypub",
                table: "collection_memberships",
                columns: new[] { "collection_iri", "object_iri" },
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_collection_memberships_add_activity",
                schema: "activitypub",
                table: "collection_memberships",
                column: "add_activity_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_collection_memberships_remove_activity",
                schema: "activitypub",
                table: "collection_memberships",
                column: "remove_activity_iri",
                unique: true,
                filter: "remove_activity_iri IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_endpoint_changes_delivery",
                schema: "activitypub",
                table: "delivery_endpoint_changes",
                columns: new[] { "delivery_id", "discovered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_targets_actor",
                schema: "activitypub",
                table: "delivery_targets",
                column: "actor_iri");

            migrationBuilder.CreateIndex(
                name: "ux_delivery_targets_delivery_actor",
                schema: "activitypub",
                table: "delivery_targets",
                columns: new[] { "delivery_id", "actor_iri" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_holds_expiry",
                schema: "activitypub",
                table: "legal_holds",
                columns: new[] { "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_legal_holds_active_resource",
                schema: "activitypub",
                table: "legal_holds",
                columns: new[] { "resource_kind", "resource_id" },
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_like_relations_active_pair",
                schema: "activitypub",
                table: "like_relations",
                columns: new[] { "actor_iri", "object_iri" },
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_like_relations_activity",
                schema: "activitypub",
                table: "like_relations",
                column: "activity_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_remote_media_cache_expiry",
                schema: "activitypub",
                table: "remote_media_cache",
                columns: new[] { "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_remote_media_cache_media_id",
                schema: "activitypub",
                table: "remote_media_cache",
                column: "media_id");

            migrationBuilder.CreateIndex(
                name: "ux_remote_media_cache_object_token",
                schema: "activitypub",
                table: "remote_media_cache",
                columns: new[] { "object_id", "source_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_mutes_owner_expiry",
                schema: "activitypub",
                table: "user_mutes",
                columns: new[] { "owner_actor_iri", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_user_mutes_active_pair",
                schema: "activitypub",
                table: "user_mutes",
                columns: new[] { "owner_actor_iri", "target_actor_iri" },
                unique: true,
                filter: "revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actor_moves",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "announce_relations",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "collection_memberships",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "delivery_endpoint_changes",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "delivery_targets",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "legal_holds",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "like_relations",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "remote_media_cache",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "user_mutes",
                schema: "activitypub");

            migrationBuilder.DropColumn(
                name: "audit_raw_json",
                schema: "activitypub",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "raw_json_purged_at",
                schema: "activitypub",
                table: "objects");

            migrationBuilder.DropColumn(
                name: "audit_raw_json",
                schema: "activitypub",
                table: "object_revisions");

            migrationBuilder.DropColumn(
                name: "raw_json_purged_at",
                schema: "activitypub",
                table: "object_revisions");

            migrationBuilder.DropColumn(
                name: "audit_raw_json",
                schema: "activitypub",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "raw_json_purged_at",
                schema: "activitypub",
                table: "activities");
        }
    }
}

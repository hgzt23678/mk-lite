using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialActivityPubSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "activitypub");

            migrationBuilder.CreateTable(
                name: "activities",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    object_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    raw_json = table.Column<string>(type: "jsonb", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_transient = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "actor_keys",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    owner_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    public_key_pem = table.Column<string>(type: "text", nullable: false),
                    algorithm = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_local = table.Column<bool>(type: "boolean", nullable: false),
                    private_key_handle = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actor_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "actor_policies",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actor_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    target = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    details_json = table.Column<string>(type: "jsonb", nullable: false),
                    previous_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    event_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dead_letters",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replayed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replayed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "domain_policies",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "follow_relations",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    follower_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    followed_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    follow_activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    decision_activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_follow_relations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_conflicts",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    existing_payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    incoming_payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    incoming_body = table.Column<byte[]>(type: "bytea", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_conflicts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_items",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    activity_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    raw_body = table.Column<byte[]>(type: "bytea", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    signature_profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    key_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    signature_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_quarantined = table.Column<bool>(type: "boolean", nullable: false),
                    quarantine_reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    last_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    detected_media_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    duration_milliseconds = table.Column<long>(type: "bigint", nullable: true),
                    thumbnail_storage_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quarantine_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_actions",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    operator_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_actions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "objects",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    owner_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    raw_json = table.Column<string>(type: "jsonb", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_objects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remote_actors",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    origin = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    preferred_username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    raw_json = table.Column<string>(type: "jsonb", nullable: false),
                    etag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_actors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remote_endpoints",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    endpoint_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    remote_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_endpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remote_key_cache",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    owner_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    public_key_pem = table.Column<string>(type: "text", nullable: false),
                    algorithm = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_document_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refresh_blocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_key_cache", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    reporter_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    target_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    raw_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schema_compatibility",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    minimum_application_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    maximum_application_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schema_compatibility", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "worker_heartbeats",
                schema: "activitypub",
                columns: table => new
                {
                    worker_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    worker_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_heartbeats", x => new { x.worker_id, x.worker_type });
                });

            migrationBuilder.CreateTable(
                name: "activity_recipients",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    field = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_recipients", x => x.id);
                    table.ForeignKey(
                        name: "FK_activity_recipients_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "activitypub",
                        principalTable: "activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    endpoint_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    remote_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    signature_profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    endpoint_rediscovery_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    last_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "FK_deliveries_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "activitypub",
                        principalTable: "activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_actors",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary_html = table.Column<string>(type: "text", nullable: false),
                    manually_approves_followers = table.Column<bool>(type: "boolean", nullable: false),
                    discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    indexable = table.Column<bool>(type: "boolean", nullable: false),
                    is_suspended = table.Column<bool>(type: "boolean", nullable: false),
                    active_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_actors", x => x.id);
                    table.ForeignKey(
                        name: "FK_local_actors_actor_keys_active_key_id",
                        column: x => x.active_key_id,
                        principalSchema: "activitypub",
                        principalTable: "actor_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "object_revisions",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    raw_json = table.Column<string>(type: "jsonb", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_object_revisions_objects_object_id",
                        column: x => x.object_id,
                        principalSchema: "activitypub",
                        principalTable: "objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    duration_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "activitypub",
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "activitypub",
                table: "schema_compatibility",
                columns: new[] { "id", "maximum_application_version", "minimum_application_version", "updated_at" },
                values: new object[] { (short)1, "1.999.999", "1.0.0", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "ix_activities_actor_occurred",
                schema: "activitypub",
                table: "activities",
                columns: new[] { "actor_iri", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activities_object_iri",
                schema: "activitypub",
                table: "activities",
                column: "object_iri");

            migrationBuilder.CreateIndex(
                name: "ux_activities_iri",
                schema: "activitypub",
                table: "activities",
                column: "iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_recipients_recipient",
                schema: "activitypub",
                table: "activity_recipients",
                column: "recipient_iri");

            migrationBuilder.CreateIndex(
                name: "ux_activity_recipients",
                schema: "activitypub",
                table: "activity_recipients",
                columns: new[] { "activity_id", "recipient_iri", "field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actor_keys_owner_state",
                schema: "activitypub",
                table: "actor_keys",
                columns: new[] { "owner_iri", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_actor_keys_key_iri",
                schema: "activitypub",
                table: "actor_keys",
                column: "key_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actor_policies_actor_kind",
                schema: "activitypub",
                table: "actor_policies",
                columns: new[] { "actor_iri", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_created",
                schema: "activitypub",
                table: "audit_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ux_audit_events_hash",
                schema: "activitypub",
                table: "audit_events",
                column: "event_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_dead_letters_source",
                schema: "activitypub",
                table: "dead_letters",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_claim",
                schema: "activitypub",
                table: "deliveries",
                columns: new[] { "state", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_domain_claim",
                schema: "activitypub",
                table: "deliveries",
                columns: new[] { "remote_domain", "state", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_lease_expiry",
                schema: "activitypub",
                table: "deliveries",
                column: "lease_expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_deliveries_activity_endpoint",
                schema: "activitypub",
                table: "deliveries",
                columns: new[] { "activity_id", "endpoint_iri" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_delivery_attempts_number",
                schema: "activitypub",
                table: "delivery_attempts",
                columns: new[] { "delivery_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_domain_policies_domain_kind",
                schema: "activitypub",
                table: "domain_policies",
                columns: new[] { "domain", "kind" });

            migrationBuilder.CreateIndex(
                name: "ux_follow_relations_activity",
                schema: "activitypub",
                table: "follow_relations",
                column: "follow_activity_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_follow_relations_pair",
                schema: "activitypub",
                table: "follow_relations",
                columns: new[] { "follower_iri", "followed_iri" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_inbox_conflicts_payload",
                schema: "activitypub",
                table: "inbox_conflicts",
                columns: new[] { "activity_iri", "incoming_payload_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_items_claim",
                schema: "activitypub",
                table: "inbox_items",
                columns: new[] { "state", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_items_lease_expiry",
                schema: "activitypub",
                table: "inbox_items",
                column: "lease_expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_inbox_items_activity_iri",
                schema: "activitypub",
                table: "inbox_items",
                column: "activity_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_actors_active_key_id",
                schema: "activitypub",
                table: "local_actors",
                column: "active_key_id");

            migrationBuilder.CreateIndex(
                name: "ux_local_actors_iri",
                schema: "activitypub",
                table: "local_actors",
                column: "iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_local_actors_username",
                schema: "activitypub",
                table: "local_actors",
                column: "normalized_username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_content_hash",
                schema: "activitypub",
                table: "media",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ux_media_storage_key",
                schema: "activitypub",
                table: "media",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_target",
                schema: "activitypub",
                table: "moderation_actions",
                columns: new[] { "target", "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_object_revisions_version",
                schema: "activitypub",
                table: "object_revisions",
                columns: new[] { "object_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_objects_owner_published",
                schema: "activitypub",
                table: "objects",
                columns: new[] { "owner_iri", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ux_objects_iri",
                schema: "activitypub",
                table: "objects",
                column: "iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_remote_actors_origin",
                schema: "activitypub",
                table: "remote_actors",
                column: "origin");

            migrationBuilder.CreateIndex(
                name: "ux_remote_actors_iri",
                schema: "activitypub",
                table: "remote_actors",
                column: "iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_remote_endpoints_iri",
                schema: "activitypub",
                table: "remote_endpoints",
                column: "endpoint_iri");

            migrationBuilder.CreateIndex(
                name: "ux_remote_endpoints_actor_kind",
                schema: "activitypub",
                table: "remote_endpoints",
                columns: new[] { "actor_iri", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_remote_key_cache_expiry",
                schema: "activitypub",
                table: "remote_key_cache",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_remote_key_cache_key_iri",
                schema: "activitypub",
                table: "remote_key_cache",
                column: "key_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_reports_iri",
                schema: "activitypub",
                table: "reports",
                column: "iri",
                unique: true,
                filter: "iri IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_worker_heartbeats_type_seen",
                schema: "activitypub",
                table: "worker_heartbeats",
                columns: new[] { "worker_type", "last_seen_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_recipients",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "actor_policies",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "dead_letters",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "delivery_attempts",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "domain_policies",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "follow_relations",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "inbox_conflicts",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "inbox_items",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "local_actors",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "media",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "moderation_actions",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "object_revisions",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "remote_actors",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "remote_endpoints",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "remote_key_cache",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "reports",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "schema_compatibility",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "worker_heartbeats",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "actor_keys",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "objects",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "activities",
                schema: "activitypub");
        }
    }
}

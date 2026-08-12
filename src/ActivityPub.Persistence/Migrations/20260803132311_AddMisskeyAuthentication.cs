using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMisskeyAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "misskey_auth_sessions",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    client_icon_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    client_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    callback_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    permissions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    encrypted_token = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_misskey_auth_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "misskey_access_tokens",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    permissions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_misskey_access_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_misskey_access_tokens_misskey_auth_sessions_source_session_~",
                        column: x => x.source_session_id,
                        principalSchema: "activitypub",
                        principalTable: "misskey_auth_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_misskey_access_tokens_actor_state",
                schema: "activitypub",
                table: "misskey_access_tokens",
                columns: new[] { "actor_iri", "revoked_at", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_misskey_access_tokens_hash",
                schema: "activitypub",
                table: "misskey_access_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_misskey_access_tokens_session",
                schema: "activitypub",
                table: "misskey_access_tokens",
                column: "source_session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_misskey_auth_sessions_state_expiry",
                schema: "activitypub",
                table: "misskey_auth_sessions",
                columns: new[] { "state", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_misskey_auth_sessions_key",
                schema: "activitypub",
                table: "misskey_auth_sessions",
                column: "session_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_misskey_auth_sessions_token",
                schema: "activitypub",
                table: "misskey_auth_sessions",
                column: "issued_token_id",
                unique: true,
                filter: "issued_token_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "misskey_access_tokens",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "misskey_auth_sessions",
                schema: "activitypub");
        }
    }
}

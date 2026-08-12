using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureReplayProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "signature_replays",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    nonce_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    key_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signature_replays", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_signature_replays_expiry",
                schema: "activitypub",
                table: "signature_replays",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_signature_replays_fingerprint",
                schema: "activitypub",
                table: "signature_replays",
                column: "fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_signature_replays_nonce",
                schema: "activitypub",
                table: "signature_replays",
                columns: new[] { "key_iri", "nonce_hash" },
                unique: true,
                filter: "nonce_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signature_replays",
                schema: "activitypub");
        }
    }
}

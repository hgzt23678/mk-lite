using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamingConnectionLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stream_connection_leases",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    remote_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stream_connection_leases", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stream_connection_leases_address_expiry",
                schema: "activitypub",
                table: "stream_connection_leases",
                columns: new[] { "remote_address", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stream_connection_leases_expiry",
                schema: "activitypub",
                table: "stream_connection_leases",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_stream_connection_leases_subject_expiry",
                schema: "activitypub",
                table: "stream_connection_leases",
                columns: new[] { "subject", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stream_connection_leases",
                schema: "activitypub");
        }
    }
}

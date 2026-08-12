using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteDomainExecutionControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "domain_delivery_leases",
                schema: "activitypub",
                columns: table => new
                {
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_delivery_leases", x => new { x.domain, x.slot });
                });

            migrationBuilder.CreateTable(
                name: "remote_domain_circuits",
                schema: "activitypub",
                columns: table => new
                {
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    open_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_domain_circuits", x => x.domain);
                });

            migrationBuilder.CreateIndex(
                name: "ix_domain_delivery_leases_expiry",
                schema: "activitypub",
                table: "domain_delivery_leases",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_domain_delivery_leases_delivery",
                schema: "activitypub",
                table: "domain_delivery_leases",
                column: "delivery_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_remote_domain_circuits_open_until",
                schema: "activitypub",
                table: "remote_domain_circuits",
                column: "open_until");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "domain_delivery_leases",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "remote_domain_circuits",
                schema: "activitypub");
        }
    }
}

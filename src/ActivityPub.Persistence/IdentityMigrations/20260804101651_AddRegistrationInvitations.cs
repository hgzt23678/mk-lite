using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.IdentityMigrations
{
    /// <inheritdoc />
    public partial class AddRegistrationInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.Sql("SET LOCAL statement_timeout = '60s';");

            migrationBuilder.CreateTable(
                name: "registration_invitations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reservation_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_by_username = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registration_invitations", x => x.id);
                    table.CheckConstraint(
                        "ck_identity_registration_invitations_code_hash_length",
                        "octet_length(code_hash) = 32");
                    table.CheckConstraint(
                        "ck_identity_registration_invitations_consumption",
                        "(consumed_at IS NULL AND consumed_by_username IS NULL) OR " +
                        "(consumed_at IS NOT NULL AND consumed_by_username IS NOT NULL)");
                    table.CheckConstraint(
                        "ck_identity_registration_invitations_expiry",
                        "expires_at > created_at");
                    table.CheckConstraint(
                        "ck_identity_registration_invitations_reservation",
                        "(reservation_id IS NULL AND reserved_at IS NULL AND reservation_expires_at IS NULL) OR " +
                        "(reservation_id IS NOT NULL AND reserved_at IS NOT NULL AND reservation_expires_at IS NOT NULL " +
                        "AND reservation_expires_at > reserved_at)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_registration_invitations_expiry",
                schema: "identity",
                table: "registration_invitations",
                columns: new[] { "expires_at", "consumed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_identity_registration_invitations_code_hash",
                schema: "identity",
                table: "registration_invitations",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_identity_registration_invitations_reservation_id",
                schema: "identity",
                table: "registration_invitations",
                column: "reservation_id",
                unique: true,
                filter: "reservation_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.DropTable(
                name: "registration_invitations",
                schema: "identity");
        }
    }
}

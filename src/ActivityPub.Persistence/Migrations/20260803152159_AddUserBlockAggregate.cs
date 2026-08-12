using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBlockAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_blocks",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    target_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    block_activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    undo_activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_blocks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_blocks_target_state",
                schema: "activitypub",
                table: "user_blocks",
                columns: new[] { "target_actor_iri", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_user_blocks_active_pair",
                schema: "activitypub",
                table: "user_blocks",
                columns: new[] { "owner_actor_iri", "target_actor_iri" },
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_user_blocks_activity_iri",
                schema: "activitypub",
                table: "user_blocks",
                column: "block_activity_iri",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_blocks",
                schema: "activitypub");
        }
    }
}

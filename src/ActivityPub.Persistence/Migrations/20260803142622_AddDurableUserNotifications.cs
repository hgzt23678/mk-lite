using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableUserNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recipient_actor_iri",
                schema: "activitypub",
                table: "stream_events",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_notifications",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    source_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    object_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    reaction = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_recipient_created",
                schema: "activitypub",
                table: "user_notifications",
                columns: new[] { "recipient_actor_iri", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_recipient_state",
                schema: "activitypub",
                table: "user_notifications",
                columns: new[] { "recipient_actor_iri", "read_at", "dismissed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_user_notifications_activity_kind",
                schema: "activitypub",
                table: "user_notifications",
                columns: new[] { "recipient_actor_iri", "activity_iri", "kind" },
                unique: true,
                filter: "activity_iri IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_notifications",
                schema: "activitypub");

            migrationBuilder.DropColumn(
                name: "recipient_actor_iri",
                schema: "activitypub",
                table: "stream_events");
        }
    }
}

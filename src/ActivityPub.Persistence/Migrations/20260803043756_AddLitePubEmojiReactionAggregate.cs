using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLitePubEmojiReactionAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "emoji_reaction_relations",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    object_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    reaction = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    custom_emoji_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    custom_emoji_name = table.Column<string>(type: "character varying(68)", maxLength: 68, nullable: true),
                    custom_emoji_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    custom_emoji_media_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emoji_reaction_relations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_emoji_reaction_relations_object_state",
                schema: "activitypub",
                table: "emoji_reaction_relations",
                columns: new[] { "object_iri", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_emoji_reaction_relations_active_reaction",
                schema: "activitypub",
                table: "emoji_reaction_relations",
                columns: new[] { "actor_iri", "object_iri", "reaction" },
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_emoji_reaction_relations_activity",
                schema: "activitypub",
                table: "emoji_reaction_relations",
                column: "activity_iri",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emoji_reaction_relations",
                schema: "activitypub");
        }
    }
}

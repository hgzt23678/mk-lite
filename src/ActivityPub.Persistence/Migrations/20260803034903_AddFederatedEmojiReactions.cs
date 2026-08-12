using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFederatedEmojiReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_emoji_iri",
                schema: "activitypub",
                table: "like_relations",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_emoji_media_type",
                schema: "activitypub",
                table: "like_relations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_emoji_name",
                schema: "activitypub",
                table: "like_relations",
                type: "character varying(68)",
                maxLength: 68,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_emoji_url",
                schema: "activitypub",
                table: "like_relations",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reaction",
                schema: "activitypub",
                table: "like_relations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_emoji_iri",
                schema: "activitypub",
                table: "like_relations");

            migrationBuilder.DropColumn(
                name: "custom_emoji_media_type",
                schema: "activitypub",
                table: "like_relations");

            migrationBuilder.DropColumn(
                name: "custom_emoji_name",
                schema: "activitypub",
                table: "like_relations");

            migrationBuilder.DropColumn(
                name: "custom_emoji_url",
                schema: "activitypub",
                table: "like_relations");

            migrationBuilder.DropColumn(
                name: "reaction",
                schema: "activitypub",
                table: "like_relations");
        }
    }
}

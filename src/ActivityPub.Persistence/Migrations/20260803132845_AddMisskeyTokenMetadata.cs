using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMisskeyTokenMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "activitypub",
                table: "misskey_access_tokens",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon_uri",
                schema: "activitypub",
                table: "misskey_access_tokens",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                schema: "activitypub",
                table: "misskey_access_tokens");

            migrationBuilder.DropColumn(
                name: "icon_uri",
                schema: "activitypub",
                table: "misskey_access_tokens");
        }
    }
}

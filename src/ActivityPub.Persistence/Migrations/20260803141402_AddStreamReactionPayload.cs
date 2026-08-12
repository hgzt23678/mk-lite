using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamReactionPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reaction",
                schema: "activitypub",
                table: "stream_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "reaction_removed",
                schema: "activitypub",
                table: "stream_events",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reaction",
                schema: "activitypub",
                table: "stream_events");

            migrationBuilder.DropColumn(
                name: "reaction_removed",
                schema: "activitypub",
                table: "stream_events");
        }
    }
}

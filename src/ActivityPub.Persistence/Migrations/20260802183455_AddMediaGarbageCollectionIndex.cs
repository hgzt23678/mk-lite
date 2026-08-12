using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaGarbageCollectionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_media_gc",
                schema: "activitypub",
                table: "media",
                columns: new[] { "state", "purged_at", "updated_at" })
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_media_gc",
                schema: "activitypub",
                table: "media")
                .Annotation("Npgsql:CreatedConcurrently", true);
        }
    }
}

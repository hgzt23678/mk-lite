using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAttachmentAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_attachments",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_attachments_media_media_id",
                        column: x => x.media_id,
                        principalSchema: "activitypub",
                        principalTable: "media",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_media_attachments_objects_object_id",
                        column: x => x.object_id,
                        principalSchema: "activitypub",
                        principalTable: "objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_attachments_object",
                schema: "activitypub",
                table: "media_attachments",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ux_media_attachments_media_object",
                schema: "activitypub",
                table: "media_attachments",
                columns: new[] { "media_id", "object_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_attachments",
                schema: "activitypub");
        }
    }
}

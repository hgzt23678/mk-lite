using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDrive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "comment",
                schema: "activitypub",
                table: "media",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                schema: "activitypub",
                table: "media",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_sensitive",
                schema: "activitypub",
                table: "media",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "drive_folders",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drive_folders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_owner_folder",
                schema: "activitypub",
                table: "media",
                columns: new[] { "owner_actor_iri", "folder_id" });

            migrationBuilder.CreateIndex(
                name: "ix_drive_folders_owner_parent",
                schema: "activitypub",
                table: "drive_folders",
                columns: new[] { "owner_actor_iri", "parent_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drive_folders",
                schema: "activitypub");

            migrationBuilder.DropIndex(
                name: "ix_media_owner_folder",
                schema: "activitypub",
                table: "media");

            migrationBuilder.DropColumn(
                name: "comment",
                schema: "activitypub",
                table: "media");

            migrationBuilder.DropColumn(
                name: "folder_id",
                schema: "activitypub",
                table: "media");

            migrationBuilder.DropColumn(
                name: "is_sensitive",
                schema: "activitypub",
                table: "media");
        }
    }
}

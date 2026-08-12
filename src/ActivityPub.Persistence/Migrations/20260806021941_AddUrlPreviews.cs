using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "url_previews",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    thumbnail = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    icon = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    site_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    player_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    player_width = table.Column<int>(type: "integer", nullable: true),
                    player_height = table.Column<int>(type: "integer", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_url_previews", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_url_previews_url",
                schema: "activitypub",
                table: "url_previews",
                column: "url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "url_previews",
                schema: "activitypub");
        }
    }
}

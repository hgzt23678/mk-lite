using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxRecipientSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_item_recipients",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbox_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_item_recipients", x => x.id);
                    table.ForeignKey(
                        name: "FK_inbox_item_recipients_inbox_items_inbox_item_id",
                        column: x => x.inbox_item_id,
                        principalSchema: "activitypub",
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_item_recipients_actor",
                schema: "activitypub",
                table: "inbox_item_recipients",
                column: "actor_iri");

            migrationBuilder.CreateIndex(
                name: "ux_inbox_item_recipients_item_actor",
                schema: "activitypub",
                table: "inbox_item_recipients",
                columns: new[] { "inbox_item_id", "actor_iri" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_item_recipients",
                schema: "activitypub");
        }
    }
}

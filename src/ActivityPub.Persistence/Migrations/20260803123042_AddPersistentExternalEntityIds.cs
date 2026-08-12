using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentExternalEntityIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "external_mastodon_id_seq",
                schema: "activitypub");

            migrationBuilder.CreateSequence(
                name: "external_misskey_id_seq",
                schema: "activitypub");

            migrationBuilder.CreateTable(
                name: "external_entity_ids",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dialect = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    internal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sort_ordinal = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_entity_ids", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_entity_ids_dialect_type_sort",
                schema: "activitypub",
                table: "external_entity_ids",
                columns: new[] { "dialect", "entity_type", "sort_ordinal" });

            migrationBuilder.CreateIndex(
                name: "ux_external_entity_ids_dialect_type_external",
                schema: "activitypub",
                table: "external_entity_ids",
                columns: new[] { "dialect", "entity_type", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_external_entity_ids_dialect_type_internal",
                schema: "activitypub",
                table: "external_entity_ids",
                columns: new[] { "dialect", "entity_type", "internal_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_entity_ids",
                schema: "activitypub");

            migrationBuilder.DropSequence(
                name: "external_mastodon_id_seq",
                schema: "activitypub");

            migrationBuilder.DropSequence(
                name: "external_misskey_id_seq",
                schema: "activitypub");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionPollVoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "poll_choice_index",
                schema: "activitypub",
                table: "stream_events",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "question_polls",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    multiple = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    baseline_voters_count = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_polls", x => x.id);
                    table.ForeignKey(
                        name: "FK_question_polls_objects_question_object_id",
                        column: x => x.question_object_id,
                        principalSchema: "activitypub",
                        principalTable: "objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "poll_options",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    choice_index = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    baseline_votes_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_options", x => x.id);
                    table.UniqueConstraint("AK_poll_options_poll_id_choice_index", x => new { x.poll_id, x.choice_index });
                    table.ForeignKey(
                        name: "FK_poll_options_question_polls_poll_id",
                        column: x => x.poll_id,
                        principalSchema: "activitypub",
                        principalTable: "question_polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "poll_votes",
                schema: "activitypub",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voter_actor_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    choice_index = table.Column<int>(type: "integer", nullable: false),
                    ballot_key = table.Column<int>(type: "integer", nullable: false),
                    activity_iri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_votes", x => x.id);
                    table.ForeignKey(
                        name: "FK_poll_votes_poll_options_poll_id_choice_index",
                        columns: x => new { x.poll_id, x.choice_index },
                        principalSchema: "activitypub",
                        principalTable: "poll_options",
                        principalColumns: new[] { "poll_id", "choice_index" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_poll_votes_question_polls_poll_id",
                        column: x => x.poll_id,
                        principalSchema: "activitypub",
                        principalTable: "question_polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_poll_votes_poll_choice_created",
                schema: "activitypub",
                table: "poll_votes",
                columns: new[] { "poll_id", "choice_index", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_poll_votes_activity",
                schema: "activitypub",
                table: "poll_votes",
                column: "activity_iri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_poll_votes_ballot",
                schema: "activitypub",
                table: "poll_votes",
                columns: new[] { "poll_id", "voter_actor_iri", "ballot_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_question_polls_expiry",
                schema: "activitypub",
                table: "question_polls",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_question_polls_object",
                schema: "activitypub",
                table: "question_polls",
                column: "question_object_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "poll_votes",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "poll_options",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "question_polls",
                schema: "activitypub");

            migrationBuilder.DropColumn(
                name: "poll_choice_index",
                schema: "activitypub",
                table: "stream_events");
        }
    }
}

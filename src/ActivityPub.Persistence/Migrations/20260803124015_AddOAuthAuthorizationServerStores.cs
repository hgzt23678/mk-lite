using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthAuthorizationServerStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oauth_applications",
                schema: "activitypub",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConsentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedirectUris = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_scopes",
                schema: "activitypub",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Descriptions = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_scopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_authorizations",
                schema: "activitypub",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_oauth_authorizations_oauth_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "activitypub",
                        principalTable: "oauth_applications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "oauth_tokens",
                schema: "activitypub",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_oauth_tokens_oauth_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "activitypub",
                        principalTable: "oauth_applications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_oauth_tokens_oauth_authorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalSchema: "activitypub",
                        principalTable: "oauth_authorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_oauth_applications_ClientId",
                schema: "activitypub",
                table: "oauth_applications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oauth_authorizations_ApplicationId_Status_Subject_Type",
                schema: "activitypub",
                table: "oauth_authorizations",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_oauth_scopes_Name",
                schema: "activitypub",
                table: "oauth_scopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oauth_tokens_ApplicationId_Status_Subject_Type",
                schema: "activitypub",
                table: "oauth_tokens",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_oauth_tokens_AuthorizationId",
                schema: "activitypub",
                table: "oauth_tokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_oauth_tokens_ReferenceId",
                schema: "activitypub",
                table: "oauth_tokens",
                column: "ReferenceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oauth_scopes",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "oauth_tokens",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "oauth_authorizations",
                schema: "activitypub");

            migrationBuilder.DropTable(
                name: "oauth_applications",
                schema: "activitypub");
        }
    }
}

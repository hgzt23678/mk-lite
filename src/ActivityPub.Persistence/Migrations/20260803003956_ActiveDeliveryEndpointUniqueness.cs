using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityPub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActiveDeliveryEndpointUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX CONCURRENTLY ux_deliveries_activity_endpoint_active_v2
                ON activitypub.deliveries (activity_id, endpoint_iri)
                WHERE state IN ('Pending', 'Leased');
                """,
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY activitypub.ux_deliveries_activity_endpoint;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER INDEX activitypub.ux_deliveries_activity_endpoint_active_v2 RENAME TO ux_deliveries_activity_endpoint;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX CONCURRENTLY ux_deliveries_activity_endpoint_all_v1
                ON activitypub.deliveries (activity_id, endpoint_iri);
                """,
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY activitypub.ux_deliveries_activity_endpoint;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "ALTER INDEX activitypub.ux_deliveries_activity_endpoint_all_v1 RENAME TO ux_deliveries_activity_endpoint;",
                suppressTransaction: true);
        }
    }
}

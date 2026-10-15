using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEventApplicationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "audit_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE audit_events
                SET "ApplicationId" = "PayloadJson"->>'application_id'
                WHERE "ApplicationId" IS NULL
                  AND jsonb_typeof("PayloadJson") = 'object'
                  AND coalesce("PayloadJson"->>'application_id', '') <> '';

                UPDATE audit_events a
                SET "ApplicationId" = d."ApplicationId"
                FROM authorization_decisions d
                WHERE a."ApplicationId" IS NULL
                  AND a."DecisionId" IS NOT NULL
                  AND a."DecisionId" = d."Id"
                  AND a."OrganizationId" = d."OrganizationId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OrganizationId_ApplicationId_CreatedAt",
                table: "audit_events",
                columns: new[] { "OrganizationId", "ApplicationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_events_OrganizationId_ApplicationId_CreatedAt",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "audit_events");
        }
    }
}

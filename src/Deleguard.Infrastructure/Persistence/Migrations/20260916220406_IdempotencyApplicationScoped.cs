using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyApplicationScoped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_records",
                table: "idempotency_records");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "idempotency_records",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE idempotency_records r
                SET "ApplicationId" = d."ApplicationId"
                FROM authorization_decisions d
                WHERE d."Id" = r."DecisionId"
                  AND r."ApplicationId" IS NULL;

                UPDATE idempotency_records r
                SET "ApplicationId" = a."Id"
                FROM organization_applications a
                WHERE a."OrganizationId" = r."OrganizationId"
                  AND a."Kind" = 'product'
                  AND a."Slug" = 'default'
                  AND r."ApplicationId" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationId",
                table: "idempotency_records",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_records",
                table: "idempotency_records",
                columns: new[] { "OrganizationId", "ApplicationId", "Key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_records",
                table: "idempotency_records");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "idempotency_records");

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_records",
                table: "idempotency_records",
                columns: new[] { "OrganizationId", "Key" });
        }
    }
}

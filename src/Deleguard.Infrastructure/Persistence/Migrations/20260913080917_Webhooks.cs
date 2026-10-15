using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Webhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_delivery_attempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndpointId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_delivery_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoint_secrets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndpointId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ciphertext = table.Column<string>(type: "text", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_endpoint_secrets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EventTypesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    FailureStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_endpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_messages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndpointId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OutboxEventId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_delivery_attempts_EndpointId_FinishedAt",
                table: "webhook_delivery_attempts",
                columns: new[] { "EndpointId", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_delivery_attempts_MessageId",
                table: "webhook_delivery_attempts",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_delivery_attempts_OrganizationId",
                table: "webhook_delivery_attempts",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_endpoint_secrets_EndpointId_Version",
                table: "webhook_endpoint_secrets",
                columns: new[] { "EndpointId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_endpoint_secrets_OrganizationId",
                table: "webhook_endpoint_secrets",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_endpoints_OrganizationId",
                table: "webhook_endpoints",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_endpoints_OrganizationId_Status",
                table: "webhook_endpoints",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_messages_EndpointId_Status",
                table: "webhook_messages",
                columns: new[] { "EndpointId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_messages_OrganizationId",
                table: "webhook_messages",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_messages_OutboxEventId_EndpointId",
                table: "webhook_messages",
                columns: new[] { "OutboxEventId", "EndpointId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_messages_Status_NextAttemptAt",
                table: "webhook_messages",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_delivery_attempts");

            migrationBuilder.DropTable(
                name: "webhook_endpoint_secrets");

            migrationBuilder.DropTable(
                name: "webhook_endpoints");

            migrationBuilder.DropTable(
                name: "webhook_messages");
        }
    }
}

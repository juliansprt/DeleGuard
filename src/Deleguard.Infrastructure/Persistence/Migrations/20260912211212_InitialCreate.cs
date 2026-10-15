using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "action_definitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "text", nullable: false),
                    Risk = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_credentials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnvironmentId = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "approval_events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovalRequestId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecisionId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApproverRolesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CanonicalFactsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    DecisionId = table.Column<string>(type: "text", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    PreviousEventHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "authorization_decisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnvironmentId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    AgentExternalId = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    ActorType = table.Column<string>(type: "text", nullable: true),
                    DelegationId = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ResourceType = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    ResourceAttributesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResourceSnapshotHash = table.Column<string>(type: "text", nullable: true),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    PolicyId = table.Column<string>(type: "text", nullable: true),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: true),
                    MatchedRule = table.Column<string>(type: "text", nullable: true),
                    RequestDigest = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    PermitId = table.Column<string>(type: "text", nullable: true),
                    ApprovalId = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "execution_evidence",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecisionId = table.Column<string>(type: "text", nullable: false),
                    PermitId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderReference = table.Column<string>(type: "text", nullable: true),
                    ResultHash = table.Column<string>(type: "text", nullable: true),
                    ResourceSnapshotHash = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_evidence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestDigest = table.Column<string>(type: "text", nullable: false),
                    DecisionId = table.Column<string>(type: "text", nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => new { x.OrganizationId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "organization_environments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_environments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "permits",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecisionId = table.Column<string>(type: "text", nullable: false),
                    Jti = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ResourceType = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    ResourceSnapshotHash = table.Column<string>(type: "text", nullable: true),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: true),
                    Token = table.Column<string>(type: "text", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "policies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PublishedVersionId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "policy_versions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Yaml = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ResourceType = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_audit_heads",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    LastEventHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_audit_heads", x => x.OrganizationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_definitions_OrganizationId",
                table: "action_definitions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_action_definitions_OrganizationId_Name",
                table: "action_definitions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_credentials_KeyHash",
                table: "agent_credentials",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_credentials_OrganizationId",
                table: "agent_credentials",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_agents_OrganizationId",
                table: "agents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_agents_OrganizationId_EnvironmentId_ExternalId",
                table: "agents",
                columns: new[] { "OrganizationId", "EnvironmentId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_events_OrganizationId",
                table: "approval_events",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_OrganizationId",
                table: "approval_requests",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_Status",
                table: "approval_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OrganizationId",
                table: "audit_events",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OrganizationId_CreatedAt",
                table: "audit_events",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OrganizationId_EventHash",
                table: "audit_events",
                columns: new[] { "OrganizationId", "EventHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_authorization_decisions_OrganizationId",
                table: "authorization_decisions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_authorization_decisions_OrganizationId_IdempotencyKey",
                table: "authorization_decisions",
                columns: new[] { "OrganizationId", "IdempotencyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_authorization_decisions_RequestDigest",
                table: "authorization_decisions",
                column: "RequestDigest");

            migrationBuilder.CreateIndex(
                name: "IX_execution_evidence_DecisionId",
                table: "execution_evidence",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_execution_evidence_OrganizationId",
                table: "execution_evidence",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_environments_OrganizationId",
                table: "organization_environments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_environments_OrganizationId_Name",
                table: "organization_environments",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_OrganizationId",
                table: "outbox_events",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_ProcessedAt",
                table: "outbox_events",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_permits_DecisionId",
                table: "permits",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_permits_Jti",
                table: "permits",
                column: "Jti",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permits_OrganizationId",
                table: "permits",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_policies_OrganizationId",
                table: "policies",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_policies_OrganizationId_Slug",
                table: "policies",
                columns: new[] { "OrganizationId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_policy_versions_OrganizationId",
                table: "policy_versions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_policy_versions_PolicyId_Version",
                table: "policy_versions",
                columns: new[] { "PolicyId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_definitions");

            migrationBuilder.DropTable(
                name: "agent_credentials");

            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "approval_events");

            migrationBuilder.DropTable(
                name: "approval_requests");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "authorization_decisions");

            migrationBuilder.DropTable(
                name: "execution_evidence");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "organization_environments");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "permits");

            migrationBuilder.DropTable(
                name: "policies");

            migrationBuilder.DropTable(
                name: "policy_versions");

            migrationBuilder.DropTable(
                name: "tenant_audit_heads");
        }
    }
}

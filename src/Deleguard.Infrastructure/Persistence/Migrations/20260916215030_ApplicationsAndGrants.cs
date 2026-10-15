using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationsAndGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_policies_OrganizationId_Slug",
                table: "policies");

            migrationBuilder.DropIndex(
                name: "IX_organization_environments_OrganizationId_Name",
                table: "organization_environments");

            migrationBuilder.DropIndex(
                name: "IX_agents_OrganizationId_EnvironmentId_ExternalId",
                table: "agents");

            migrationBuilder.DropIndex(
                name: "IX_action_definitions_OrganizationId_Name",
                table: "action_definitions");

            migrationBuilder.CreateTable(
                name: "organization_applications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_applications", x => x.Id);
                });

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "webhook_endpoints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "policies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "organization_environments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "authorization_decisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "action_definitions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                INSERT INTO organization_applications ("Id", "OrganizationId", "Slug", "Name", "Kind", "CreatedAt")
                SELECT
                    CASE WHEN o."Id" = 'org_demo' THEN 'app_demo_admin'
                         ELSE left('app_admin_' || CASE WHEN o."Id" LIKE 'org_%' THEN substring(o."Id" FROM 5) ELSE o."Id" END, 64)
                    END,
                    o."Id",
                    'administration',
                    'Administration',
                    'administration',
                    o."CreatedAt"
                FROM organizations o
                WHERE NOT EXISTS (
                    SELECT 1 FROM organization_applications a
                    WHERE a."OrganizationId" = o."Id" AND a."Kind" = 'administration');

                INSERT INTO organization_applications ("Id", "OrganizationId", "Slug", "Name", "Kind", "CreatedAt")
                SELECT
                    CASE WHEN o."Id" = 'org_demo' THEN 'app_demo'
                         ELSE left('app_default_' || CASE WHEN o."Id" LIKE 'org_%' THEN substring(o."Id" FROM 5) ELSE o."Id" END, 64)
                    END,
                    o."Id",
                    'default',
                    CASE WHEN length(btrim(o."Name")) >= 3 THEN left(btrim(o."Name"), 200) ELSE 'Default' END,
                    'product',
                    o."CreatedAt"
                FROM organizations o
                WHERE NOT EXISTS (
                    SELECT 1 FROM organization_applications a
                    WHERE a."OrganizationId" = o."Id" AND a."Slug" = 'default');

                UPDATE organization_environments e
                SET "ApplicationId" = a."Id"
                FROM organization_applications a
                WHERE a."OrganizationId" = e."OrganizationId"
                  AND a."Kind" = 'product'
                  AND a."Slug" = 'default'
                  AND e."ApplicationId" IS NULL;

                INSERT INTO organization_environments ("Id", "OrganizationId", "ApplicationId", "Name", "CreatedAt")
                SELECT
                    CASE WHEN o."Id" = 'org_demo' THEN 'env_demo_admin_production'
                         ELSE left('env_admin_' || CASE WHEN o."Id" LIKE 'org_%' THEN substring(o."Id" FROM 5) ELSE o."Id" END, 64)
                    END,
                    o."Id",
                    a."Id",
                    'production',
                    o."CreatedAt"
                FROM organizations o
                JOIN organization_applications a
                  ON a."OrganizationId" = o."Id" AND a."Kind" = 'administration'
                WHERE NOT EXISTS (
                    SELECT 1 FROM organization_environments e
                    WHERE e."ApplicationId" = a."Id" AND e."Name" = 'production');

                UPDATE agents ag
                SET "ApplicationId" = e."ApplicationId"
                FROM organization_environments e
                WHERE e."Id" = ag."EnvironmentId"
                  AND ag."ApplicationId" IS NULL;

                UPDATE agents ag
                SET "ApplicationId" = a."Id",
                    "EnvironmentId" = e."Id"
                FROM organization_applications a
                JOIN organization_environments e
                  ON e."ApplicationId" = a."Id" AND e."Name" = 'production'
                WHERE a."OrganizationId" = ag."OrganizationId"
                  AND a."Kind" = 'administration'
                  AND ag."ExternalId" = 'admin-mcp';

                UPDATE action_definitions d
                SET "ApplicationId" = a."Id"
                FROM organization_applications a
                WHERE a."OrganizationId" = d."OrganizationId"
                  AND a."Kind" = 'administration'
                  AND d."Name" LIKE 'admin.%';

                UPDATE action_definitions d
                SET "ApplicationId" = a."Id"
                FROM organization_applications a
                WHERE a."OrganizationId" = d."OrganizationId"
                  AND a."Kind" = 'product'
                  AND a."Slug" = 'default'
                  AND d."Name" NOT LIKE 'admin.%';

                UPDATE policies p
                SET "ApplicationId" = a."Id"
                FROM organization_applications a
                WHERE a."OrganizationId" = p."OrganizationId"
                  AND a."Kind" = 'administration'
                  AND (
                      p."Slug" LIKE 'admin-%'
                      OR EXISTS (
                          SELECT 1 FROM policy_versions v
                          WHERE v."PolicyId" = p."Id" AND v."Action" LIKE 'admin.%'));

                UPDATE policies p
                SET "ApplicationId" = a."Id"
                FROM organization_applications a
                WHERE a."OrganizationId" = p."OrganizationId"
                  AND a."Kind" = 'product'
                  AND a."Slug" = 'default'
                  AND p."ApplicationId" IS NULL;

                UPDATE authorization_decisions d
                SET "ApplicationId" = ag."ApplicationId"
                FROM agents ag
                WHERE ag."Id" = d."AgentId"
                  AND d."ApplicationId" IS NULL;

                UPDATE authorization_decisions d
                SET "ApplicationId" = a."Id"
                FROM organization_applications a
                WHERE a."OrganizationId" = d."OrganizationId"
                  AND a."Kind" = 'product'
                  AND a."Slug" = 'default'
                  AND d."ApplicationId" IS NULL;

                UPDATE webhook_endpoints w
                SET "ApplicationId" = a."Id"
                FROM organization_applications a
                WHERE a."OrganizationId" = w."OrganizationId"
                  AND a."Kind" = 'product'
                  AND a."Slug" = 'default'
                  AND w."ApplicationId" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationId",
                table: "webhook_endpoints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationId",
                table: "policies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationId",
                table: "organization_environments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationId",
                table: "authorization_decisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationId",
                table: "agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationId",
                table: "action_definitions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.CreateTable(
                name: "agent_action_grants",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionDefinitionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_action_grants", x => new { x.OrganizationId, x.AgentId, x.ActionDefinitionId });
                });

            migrationBuilder.CreateTable(
                name: "agent_policy_grants",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_policy_grants", x => new { x.OrganizationId, x.AgentId, x.PolicyId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_policies_ApplicationId_Slug",
                table: "policies",
                columns: new[] { "ApplicationId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_environments_ApplicationId",
                table: "organization_environments",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_environments_ApplicationId_Name",
                table: "organization_environments",
                columns: new[] { "ApplicationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_authorization_decisions_OrganizationId_ApplicationId",
                table: "authorization_decisions",
                columns: new[] { "OrganizationId", "ApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_agents_ApplicationId",
                table: "agents",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_agents_ApplicationId_EnvironmentId_ExternalId",
                table: "agents",
                columns: new[] { "ApplicationId", "EnvironmentId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_definitions_ApplicationId_Name",
                table: "action_definitions",
                columns: new[] { "ApplicationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_action_grants_OrganizationId",
                table: "agent_action_grants",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_action_grants_OrganizationId_ActionDefinitionId",
                table: "agent_action_grants",
                columns: new[] { "OrganizationId", "ActionDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_action_grants_OrganizationId_AgentId",
                table: "agent_action_grants",
                columns: new[] { "OrganizationId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_policy_grants_OrganizationId",
                table: "agent_policy_grants",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_policy_grants_OrganizationId_AgentId",
                table: "agent_policy_grants",
                columns: new[] { "OrganizationId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_policy_grants_OrganizationId_PolicyId",
                table: "agent_policy_grants",
                columns: new[] { "OrganizationId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_applications_OrganizationId",
                table: "organization_applications",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_applications_OrganizationId_Kind",
                table: "organization_applications",
                columns: new[] { "OrganizationId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_applications_OrganizationId_Slug",
                table: "organization_applications",
                columns: new[] { "OrganizationId", "Slug" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO agent_action_grants ("OrganizationId", "AgentId", "ActionDefinitionId", "ApplicationId", "CreatedAt")
                SELECT d."OrganizationId", ag."Id", d."Id", d."ApplicationId", NOW()
                FROM action_definitions d
                JOIN agents ag
                  ON ag."OrganizationId" = d."OrganizationId"
                 AND ag."ApplicationId" = d."ApplicationId"
                ON CONFLICT DO NOTHING;

                INSERT INTO agent_policy_grants ("OrganizationId", "AgentId", "PolicyId", "ApplicationId", "CreatedAt")
                SELECT p."OrganizationId", ag."Id", p."Id", p."ApplicationId", NOW()
                FROM policies p
                JOIN agents ag
                  ON ag."OrganizationId" = p."OrganizationId"
                 AND ag."ApplicationId" = p."ApplicationId"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_action_grants");

            migrationBuilder.DropTable(
                name: "agent_policy_grants");

            migrationBuilder.DropTable(
                name: "organization_applications");

            migrationBuilder.DropIndex(
                name: "IX_policies_ApplicationId_Slug",
                table: "policies");

            migrationBuilder.DropIndex(
                name: "IX_organization_environments_ApplicationId",
                table: "organization_environments");

            migrationBuilder.DropIndex(
                name: "IX_organization_environments_ApplicationId_Name",
                table: "organization_environments");

            migrationBuilder.DropIndex(
                name: "IX_authorization_decisions_OrganizationId_ApplicationId",
                table: "authorization_decisions");

            migrationBuilder.DropIndex(
                name: "IX_agents_ApplicationId",
                table: "agents");

            migrationBuilder.DropIndex(
                name: "IX_agents_ApplicationId_EnvironmentId_ExternalId",
                table: "agents");

            migrationBuilder.DropIndex(
                name: "IX_action_definitions_ApplicationId_Name",
                table: "action_definitions");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "policies");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "organization_environments");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "authorization_decisions");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "action_definitions");

            migrationBuilder.CreateIndex(
                name: "IX_policies_OrganizationId_Slug",
                table: "policies",
                columns: new[] { "OrganizationId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_environments_OrganizationId_Name",
                table: "organization_environments",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agents_OrganizationId_EnvironmentId_ExternalId",
                table: "agents",
                columns: new[] { "OrganizationId", "EnvironmentId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_definitions_OrganizationId_Name",
                table: "action_definitions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);
        }
    }
}

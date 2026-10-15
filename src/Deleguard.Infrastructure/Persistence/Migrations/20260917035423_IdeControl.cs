using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdeControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ide_grants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    Directory = table.Column<string>(type: "text", nullable: false),
                    ActionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ide_grants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ide_permit_uses",
                columns: table => new
                {
                    DecisionId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    OperationId = table.Column<string>(type: "text", nullable: false),
                    SnapshotHash = table.Column<string>(type: "text", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ide_permit_uses", x => x.DecisionId);
                });

            migrationBuilder.CreateTable(
                name: "ide_projects",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CaseSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    ShareDiff = table.Column<bool>(type: "boolean", nullable: false),
                    ProtectedPathsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DeniedPathsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ide_projects", x => new { x.OrganizationId, x.ApplicationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ide_sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    Adapter = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ide_sessions", x => new { x.OrganizationId, x.ApplicationId, x.AgentId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ide_grants_OrganizationId_ApplicationId_ProjectId_AgentId",
                table: "ide_grants",
                columns: new[] { "OrganizationId", "ApplicationId", "ProjectId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ide_permit_uses_OrganizationId_ApplicationId_AgentId_Operat~",
                table: "ide_permit_uses",
                columns: new[] { "OrganizationId", "ApplicationId", "AgentId", "OperationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ide_grants");

            migrationBuilder.DropTable(
                name: "ide_permit_uses");

            migrationBuilder.DropTable(
                name: "ide_projects");

            migrationBuilder.DropTable(
                name: "ide_sessions");
        }
    }
}

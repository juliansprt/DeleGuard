using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActionMcpBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "action_mcp_bindings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionDefinitionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    McpServer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    McpTool = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceIdPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AttributePathsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_mcp_bindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_mcp_bindings_OrganizationId",
                table: "action_mcp_bindings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_action_mcp_bindings_OrganizationId_ActionDefinitionId",
                table: "action_mcp_bindings",
                columns: new[] { "OrganizationId", "ActionDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_action_mcp_bindings_OrganizationId_McpServer_McpTool",
                table: "action_mcp_bindings",
                columns: new[] { "OrganizationId", "McpServer", "McpTool" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_mcp_bindings");
        }
    }
}

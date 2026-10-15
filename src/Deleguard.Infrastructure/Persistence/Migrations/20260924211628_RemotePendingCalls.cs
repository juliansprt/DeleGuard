using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemotePendingCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "remote_guard_calls",
                columns: table => new
                {
                    DecisionId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: false),
                    EnvironmentId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    Tool = table.Column<string>(type: "text", nullable: false),
                    ProtectedArguments = table.Column<string>(type: "text", nullable: false),
                    BindingHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_guard_calls", x => x.DecisionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_remote_guard_calls_ExpiresAt",
                table: "remote_guard_calls",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_remote_guard_calls_OrganizationId_ApplicationId_AgentId",
                table: "remote_guard_calls",
                columns: new[] { "OrganizationId", "ApplicationId", "AgentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "remote_guard_calls");
        }
    }
}

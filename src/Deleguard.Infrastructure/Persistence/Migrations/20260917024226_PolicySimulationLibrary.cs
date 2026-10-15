using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PolicySimulationLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "policy_simulation_cases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExpectedEffect = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_simulation_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_policy_simulation_cases_policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_policy_simulation_cases_OrganizationId",
                table: "policy_simulation_cases",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_policy_simulation_cases_OrganizationId_ApplicationId_Policy~",
                table: "policy_simulation_cases",
                columns: new[] { "OrganizationId", "ApplicationId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_policy_simulation_cases_PolicyId",
                table: "policy_simulation_cases",
                column: "PolicyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "policy_simulation_cases");
        }
    }
}

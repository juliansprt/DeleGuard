using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gateway_model_routes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FallbackGroup = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputPricePerMillion = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    OutputPricePerMillion = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gateway_model_routes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gateway_spend_reservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReservedUsd = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    SettledUsd = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gateway_spend_reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_gateway_settings",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    RoutingMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MonthlyBudgetUsd = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    DefaultTokenBudgetClass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_gateway_settings", x => x.OrganizationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gateway_model_routes_OrganizationId",
                table: "gateway_model_routes",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_gateway_model_routes_OrganizationId_Alias",
                table: "gateway_model_routes",
                columns: new[] { "OrganizationId", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gateway_model_routes_OrganizationId_FallbackGroup_SortOrder",
                table: "gateway_model_routes",
                columns: new[] { "OrganizationId", "FallbackGroup", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_gateway_spend_reservations_OrganizationId",
                table: "gateway_spend_reservations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_gateway_spend_reservations_OrganizationId_CreatedAt",
                table: "gateway_spend_reservations",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_gateway_spend_reservations_OrganizationId_DecisionId",
                table: "gateway_spend_reservations",
                columns: new[] { "OrganizationId", "DecisionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gateway_model_routes");

            migrationBuilder.DropTable(
                name: "gateway_spend_reservations");

            migrationBuilder.DropTable(
                name: "organization_gateway_settings");
        }
    }
}

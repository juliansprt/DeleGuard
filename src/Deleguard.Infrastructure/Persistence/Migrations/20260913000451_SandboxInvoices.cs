using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SandboxInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sandbox_invoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    ReportedToTaxAuthority = table.Column<bool>(type: "boolean", nullable: false),
                    LastDecisionId = table.Column<string>(type: "text", nullable: true),
                    LastApprovalId = table.Column<string>(type: "text", nullable: true),
                    LastDecision = table.Column<string>(type: "text", nullable: true),
                    LastReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandbox_invoices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sandbox_invoices_OrganizationId",
                table: "sandbox_invoices",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_sandbox_invoices_OrganizationId_DisplayId",
                table: "sandbox_invoices",
                columns: new[] { "OrganizationId", "DisplayId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sandbox_invoices");
        }
    }
}

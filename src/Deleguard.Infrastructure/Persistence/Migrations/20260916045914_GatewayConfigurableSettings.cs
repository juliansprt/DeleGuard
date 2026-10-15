using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GatewayConfigurableSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CascadeMaxAttempts",
                table: "organization_gateway_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "CascadeMaxUsd",
                table: "organization_gateway_settings",
                type: "numeric(18,6)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "CascadeSkipToStrongTokens",
                table: "organization_gateway_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2000);

            migrationBuilder.AddColumn<int>(
                name: "DeepMaxOutputTokens",
                table: "organization_gateway_settings",
                type: "integer",
                nullable: false,
                defaultValue: 8192);

            migrationBuilder.AddColumn<int>(
                name: "ExactCacheTtlSeconds",
                table: "organization_gateway_settings",
                type: "integer",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.AddColumn<int>(
                name: "HeuristicCheapMaxTokens",
                table: "organization_gateway_settings",
                type: "integer",
                nullable: false,
                defaultValue: 400);

            migrationBuilder.AddColumn<bool>(
                name: "PrefixCacheEnabled",
                table: "organization_gateway_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShadowRouterEnabled",
                table: "organization_gateway_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ShortMaxOutputTokens",
                table: "organization_gateway_settings",
                type: "integer",
                nullable: false,
                defaultValue: 256);

            migrationBuilder.AddColumn<int>(
                name: "StandardMaxOutputTokens",
                table: "organization_gateway_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2048);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CascadeMaxAttempts",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "CascadeMaxUsd",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "CascadeSkipToStrongTokens",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "DeepMaxOutputTokens",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "ExactCacheTtlSeconds",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "HeuristicCheapMaxTokens",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "PrefixCacheEnabled",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "ShadowRouterEnabled",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "ShortMaxOutputTokens",
                table: "organization_gateway_settings");

            migrationBuilder.DropColumn(
                name: "StandardMaxOutputTokens",
                table: "organization_gateway_settings");
        }
    }
}

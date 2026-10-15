using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermitOperationBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperationHash",
                table: "permits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationId",
                table: "permits",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OperationHash",
                table: "permits");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "permits");
        }
    }
}

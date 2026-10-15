using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FederatedHumanIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<string>(
                name: "ExternalIssuer",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubject",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_ExternalIssuer_ExternalSubject",
                table: "users",
                columns: new[] { "ExternalIssuer", "ExternalSubject" },
                unique: true,
                filter: "\"ExternalIssuer\" IS NOT NULL AND \"ExternalSubject\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_ExternalIssuer_ExternalSubject",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ExternalIssuer",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ExternalSubject",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}

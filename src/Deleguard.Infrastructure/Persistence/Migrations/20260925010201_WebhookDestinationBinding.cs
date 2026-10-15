using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deleguard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhookDestinationBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE TRIGGER state_lock BEFORE INSERT OR UPDATE OR DELETE ON webhook_endpoints FOR EACH ROW EXECUTE FUNCTION deleguard_state_lock();");
            migrationBuilder.AddColumn<string>(
                name: "DestinationUrl",
                table: "webhook_messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS state_lock ON webhook_endpoints;");
            migrationBuilder.DropColumn(
                name: "DestinationUrl",
                table: "webhook_messages");
        }
    }
}

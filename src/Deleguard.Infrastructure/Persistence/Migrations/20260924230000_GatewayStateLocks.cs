using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Deleguard.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DeleguardDbContext))]
[Migration("20260924230000_GatewayStateLocks")]
public sealed class GatewayStateLocks : Migration
{
    private static readonly string[] Tables = ["organization_gateway_settings", "gateway_model_routes", "gateway_spend_reservations", "action_mcp_bindings"];
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
            migrationBuilder.Sql($"CREATE TRIGGER state_lock BEFORE INSERT OR UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION deleguard_state_lock();");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables) migrationBuilder.Sql($"DROP TRIGGER IF EXISTS state_lock ON {table};");
    }
}

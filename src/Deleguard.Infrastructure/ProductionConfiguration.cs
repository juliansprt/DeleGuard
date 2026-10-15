using Microsoft.Extensions.Configuration;
namespace Deleguard.Infrastructure;

public static class ProductionConfiguration
{
    public static void Validate(IConfiguration c)
    {
        foreach (var key in new[] { "Permits:SigningKey", "DemoAuth:ApiKeyPepper", "Webhooks:ProtectionKey" })
        {
            var value = c[key];
            if (string.IsNullOrWhiteSpace(value) || value.Length < 32 || value.Contains("change", StringComparison.OrdinalIgnoreCase) || value.Contains("deleguard-dev", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Production requires a unique secret of at least 32 characters for {key}.");
        }
        if (c.GetValue<bool>("DemoAuth:AllowEmergencyConsoleToken") || !string.IsNullOrWhiteSpace(c["DemoAuth:ConsoleToken"]) ||
            c.GetValue<bool>("Webhooks:AllowLoopback") || c.GetValue<bool>("Webhooks:AllowInsecureHttp"))
            throw new InvalidOperationException("Production forbids demo console tokens and insecure webhook exceptions.");
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(c.GetConnectionString("Postgres"));
        if (cs.Password is "deleguard" or "postgres" || cs.SslMode != Npgsql.SslMode.VerifyFull)
            throw new InvalidOperationException("Production PostgreSQL requires a non-demo password and SSL Mode=VerifyFull.");
    }
}

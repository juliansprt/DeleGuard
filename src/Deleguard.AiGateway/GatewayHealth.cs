namespace Deleguard.AiGateway;

public sealed record GatewayHealthSnapshot(string Status, bool PdpConfigured, IReadOnlyList<string> Providers);

public static class GatewayHealth
{
    public static GatewayHealthSnapshot Snapshot(IConfiguration configuration, GatewayOptions options)
    {
        var providers = configuration.GetSection("Providers").GetChildren()
            .Select(section => section.Key)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        return new GatewayHealthSnapshot(
            "ok",
            !string.IsNullOrWhiteSpace(options.Url),
            providers);
    }
}

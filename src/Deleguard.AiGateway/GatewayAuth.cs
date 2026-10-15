namespace Deleguard.AiGateway;

public static class GatewayAuth
{
    public static string? ReadBearer(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        const string prefix = "Bearer ";
        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = header[prefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        return null;
    }

    public static bool HasCredential(string? inboundBearer, string? configuredApiKey) =>
        !string.IsNullOrWhiteSpace(inboundBearer) || !string.IsNullOrWhiteSpace(configuredApiKey);

    public static string? Resolve(string? inboundBearer, string? configuredApiKey)
    {
        if (!string.IsNullOrWhiteSpace(inboundBearer))
        {
            return inboundBearer.Trim();
        }

        return string.IsNullOrWhiteSpace(configuredApiKey) ? null : configuredApiKey.Trim();
    }
}

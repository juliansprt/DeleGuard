using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;

namespace Deleguard.Infrastructure.Persistence;

internal static class DefaultGatewayCatalog
{
    public static IReadOnlyList<GatewayModelRoute> Routes(string organizationId) =>
    [
        Route(organizationId, "chat.cheap", "openai", "gpt-4.1-mini", GatewayModelTiers.Cheap, "chat", 0, 0.40m, 1.60m),
        Route(organizationId, "chat.strong", "openai", "gpt-4.1", GatewayModelTiers.Strong, "chat", 1, 2.00m, 8.00m),
        Route(organizationId, "chat.default", "openai", "gpt-4.1-mini", GatewayModelTiers.Cheap, "chat", 0, 0.40m, 1.60m),
        Route(organizationId, "embed.default", "openai", "text-embedding-3-small", GatewayModelTiers.Embed, "embed", 0, 0.02m, 0m)
    ];

    private static GatewayModelRoute Route(
        string organizationId,
        string alias,
        string provider,
        string model,
        string tier,
        string group,
        int order,
        decimal input,
        decimal output) =>
        new()
        {
            Id = IdGenerator.New("rte"),
            OrganizationId = organizationId,
            Alias = alias,
            Provider = provider,
            Model = model,
            Tier = tier,
            Region = "global",
            FallbackGroup = group,
            SortOrder = order,
            InputPricePerMillion = input,
            OutputPricePerMillion = output,
            Enabled = true
        };
}

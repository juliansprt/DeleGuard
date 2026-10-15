using System.Text.Json;

namespace Deleguard.DemoAgent.Agent;

public sealed record InducedIntent(
    string Action,
    string? ResourceId,
    string RawModelJson,
    IReadOnlyDictionary<string, JsonElement> IgnoredFields,
    string Source = "stub");

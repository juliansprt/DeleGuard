using System.Text.Json;
using System.Text.RegularExpressions;

namespace Deleguard.DemoAgent.Agent;

public sealed class StubIntentInducer : IIntentInducer
{
    private static readonly Regex InvoiceId = new(@"FAC-[A-Z0-9_-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExtraAmount = new(@"(?:amount|importe|vale)\s*[:=]?\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExtraStatus = new(@"status\s*[:=]\s*""?([a-z]+)""?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CancelVerb = new(
        @"cancel|anul|void|eliminar|delete|borrar|quita|\bremove\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<InducedIntent> InduceAsync(string userText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idMatch = InvoiceId.Match(userText ?? string.Empty);
        var resourceId = idMatch.Success ? idMatch.Value.ToUpperInvariant().TrimEnd('-', '_') : null;
        var wantsCancel = CancelVerb.IsMatch(userText ?? string.Empty);
        var action = wantsCancel && resourceId is not null ? "invoice.cancel" : "unknown";

        var ignored = ExtractClaims(userText);
        var amountMatch = ExtraAmount.Match(userText ?? string.Empty);
        var statusMatch = ExtraStatus.Match(userText ?? string.Empty);

        var raw = JsonSerializer.Serialize(new
        {
            action,
            resource_id = resourceId,
            amount = amountMatch.Success ? amountMatch.Groups[1].Value : null,
            status = statusMatch.Success ? statusMatch.Groups[1].Value : null
        });

        return Task.FromResult(new InducedIntent(action, resourceId, raw, ignored, "stub"));
    }

    public static InducedIntent MergeUserClaims(string userText, InducedIntent intent)
    {
        var claims = ExtractClaims(userText);
        if (claims.Count == 0)
        {
            return intent;
        }

        var merged = new Dictionary<string, JsonElement>(intent.IgnoredFields, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in claims)
        {
            merged.TryAdd(pair.Key, pair.Value);
        }

        return intent with { IgnoredFields = merged };
    }

    internal static Dictionary<string, JsonElement> ExtractClaims(string? userText)
    {
        var ignored = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var amountMatch = ExtraAmount.Match(userText ?? string.Empty);
        if (amountMatch.Success && long.TryParse(amountMatch.Groups[1].Value, out var amount))
        {
            ignored["amount"] = JsonSerializer.SerializeToElement(amount);
        }

        var statusMatch = ExtraStatus.Match(userText ?? string.Empty);
        if (statusMatch.Success)
        {
            ignored["status"] = JsonSerializer.SerializeToElement(statusMatch.Groups[1].Value);
        }

        return ignored;
    }
}

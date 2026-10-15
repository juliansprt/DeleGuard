using System.Text.RegularExpressions;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;

namespace Deleguard.Application.Authorization;

public static class GatewayCatalogRules
{
    public const string AliasRules = "lowercase, start with a letter, and use only a-z, 0-9, ., _, - (e.g. chat.cheap)";

    private static readonly Regex AliasPattern = new("^[a-z][a-z0-9._-]{0,127}$", RegexOptions.Compiled);
    private static readonly Regex ProviderPattern = new("^[a-z][a-z0-9-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex SlugPattern = new("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex ModelPattern = new("^[A-Za-z0-9._:/-]{1,128}$", RegexOptions.Compiled);

    public static void ValidateSettings(GatewaySettingsPatch patch)
    {
        if (!GatewayRoutingModes.IsKnown(patch.RoutingMode))
        {
            throw new RequestValidationException("routing_mode must be static, heuristic, or cascade.");
        }

        if (!GatewayTokenBudgets.IsKnown(patch.DefaultTokenBudgetClass))
        {
            throw new RequestValidationException("default_token_budget_class must be short, standard, or deep.");
        }

        if (patch.MonthlyBudgetUsd < 0)
        {
            throw new RequestValidationException("monthly_budget_usd must be zero or positive.");
        }

        ValidateTokens(patch.ShortMaxOutputTokens, "short_max_output_tokens");
        ValidateTokens(patch.StandardMaxOutputTokens, "standard_max_output_tokens");
        ValidateTokens(patch.DeepMaxOutputTokens, "deep_max_output_tokens");
        if (patch.ShortMaxOutputTokens >= patch.StandardMaxOutputTokens ||
            patch.StandardMaxOutputTokens >= patch.DeepMaxOutputTokens)
        {
            throw new RequestValidationException("Token classes must satisfy short < standard < deep.");
        }

        ValidateTokens(patch.HeuristicCheapMaxTokens, "heuristic_cheap_max_tokens");
        ValidateTokens(patch.CascadeSkipToStrongTokens, "cascade_skip_to_strong_tokens");
        if (patch.CascadeMaxAttempts is < 1 or > GatewayRoutingKnobs.MaxCascadeAttempts)
        {
            throw new RequestValidationException("cascade_max_attempts must be 1 or 2.");
        }

        if (patch.CascadeMaxUsd < 0)
        {
            throw new RequestValidationException("cascade_max_usd must be zero or positive.");
        }

        if (patch.ExactCacheTtlSeconds is < 0 or > GatewayRoutingKnobs.MaxExactCacheTtlSeconds)
        {
            throw new RequestValidationException("exact_cache_ttl_seconds must be between 0 and 86400. Zero disables the exact cache.");
        }
    }

    public static GatewayRouteWrite NormalizeRoute(GatewayRouteWrite route)
    {
        var alias = (route.Alias ?? string.Empty).Trim().ToLowerInvariant();
        var provider = (route.Provider ?? string.Empty).Trim().ToLowerInvariant();
        var model = (route.Model ?? string.Empty).Trim();
        var tier = (route.Tier ?? string.Empty).Trim().ToLowerInvariant();
        var region = (route.Region ?? string.Empty).Trim().ToLowerInvariant();
        var group = (route.FallbackGroup ?? string.Empty).Trim().ToLowerInvariant();
        if (!AliasPattern.IsMatch(alias))
        {
            throw new RequestValidationException($"alias must be {AliasRules}.");
        }

        if (!ProviderPattern.IsMatch(provider))
        {
            throw new RequestValidationException("provider must be a lowercase slug (e.g. openai).");
        }

        if (!ModelPattern.IsMatch(model))
        {
            throw new RequestValidationException("model is required and may use letters, digits, ., _, :, /, and -.");
        }

        if (!GatewayModelTiers.IsKnown(tier))
        {
            throw new RequestValidationException("tier must be cheap, strong, or embed.");
        }

        if (!SlugPattern.IsMatch(region))
        {
            throw new RequestValidationException("region must be a lowercase slug (e.g. global).");
        }

        if (!SlugPattern.IsMatch(group))
        {
            throw new RequestValidationException("fallback_group must be a lowercase slug (e.g. chat).");
        }

        if (route.InputPricePerMillion < 0 || route.OutputPricePerMillion < 0)
        {
            throw new RequestValidationException("Prices per million tokens must be zero or positive.");
        }

        if (route.SortOrder is < 0 or > 9999)
        {
            throw new RequestValidationException("sort_order must be between 0 and 9999.");
        }

        return route with
        {
            Id = string.IsNullOrWhiteSpace(route.Id) ? null : route.Id.Trim(),
            Alias = alias,
            Provider = provider,
            Model = model,
            Tier = tier,
            Region = region,
            FallbackGroup = group,
            InputPricePerMillion = decimal.Round(route.InputPricePerMillion, 6),
            OutputPricePerMillion = decimal.Round(route.OutputPricePerMillion, 6)
        };
    }

    public static void RejectSemanticCache(bool? enabled)
    {
        if (enabled == true)
        {
            throw new RequestValidationException("Semantic cache is not available for agent tool loops. Use exact-match and prefix cache only.");
        }
    }

    private static void ValidateTokens(int value, string name)
    {
        if (value is < GatewayTokenBudgets.MinOutputTokens or > GatewayTokenBudgets.MaxOutputTokensBound)
        {
            throw new RequestValidationException($"{name} must be between {GatewayTokenBudgets.MinOutputTokens} and {GatewayTokenBudgets.MaxOutputTokensBound}.");
        }
    }
}

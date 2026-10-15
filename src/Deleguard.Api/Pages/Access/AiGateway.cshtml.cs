using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Application.Identity;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Access;

public sealed class AiGatewayModel : PageModel
{
    public const string PepBaseUrl = "http://127.0.0.1:5088/v1";

    private readonly GatewayAdminHandler _gateway;
    private readonly IAdminActionGateway _admin;
    private readonly IControlQueries _queries;
    public IReadOnlyList<DecisionActivity> Activity { get; private set; } = [];

    public AiGatewayModel(GatewayAdminHandler gateway, IAdminActionGateway admin, IControlQueries queries)
    {
        _gateway = gateway;
        _admin = admin; _queries = queries;
    }

    [BindProperty]
    public bool Enabled { get; set; }

    [BindProperty]
    public string RoutingMode { get; set; } = GatewayRoutingModes.Static;

    [BindProperty]
    public decimal MonthlyBudgetUsd { get; set; } = 50;

    [BindProperty]
    public string DefaultTokenBudgetClass { get; set; } = GatewayTokenBudgets.Standard;

    [BindProperty]
    public int ShortMaxOutputTokens { get; set; } = GatewayTokenBudgets.DefaultShort;

    [BindProperty]
    public int StandardMaxOutputTokens { get; set; } = GatewayTokenBudgets.DefaultStandard;

    [BindProperty]
    public int DeepMaxOutputTokens { get; set; } = GatewayTokenBudgets.DefaultDeep;

    [BindProperty]
    public int HeuristicCheapMaxTokens { get; set; } = GatewayRoutingKnobs.HeuristicCheapMaxTokens;

    [BindProperty]
    public int CascadeSkipToStrongTokens { get; set; } = GatewayRoutingKnobs.CascadeSkipToStrongTokens;

    [BindProperty]
    public int CascadeMaxAttempts { get; set; } = GatewayRoutingKnobs.CascadeMaxAttempts;

    [BindProperty]
    public decimal CascadeMaxUsd { get; set; } = GatewayRoutingKnobs.CascadeMaxUsd;

    [BindProperty]
    public int ExactCacheTtlSeconds { get; set; } = GatewayRoutingKnobs.ExactCacheTtlSeconds;

    [BindProperty]
    public bool PrefixCacheEnabled { get; set; } = true;

    [BindProperty]
    public bool ShadowRouterEnabled { get; set; } = true;

    [BindProperty]
    public string? RouteId { get; set; }

    [BindProperty]
    public string RouteAlias { get; set; } = string.Empty;

    [BindProperty]
    public string RouteProviderPreset { get; set; } = "openai";

    [BindProperty]
    public string RouteProvider { get; set; } = "openai";

    [BindProperty]
    public string RouteModel { get; set; } = string.Empty;

    [BindProperty]
    public string RouteTier { get; set; } = GatewayModelTiers.Cheap;

    [BindProperty]
    public string RouteRegion { get; set; } = "global";

    [BindProperty]
    public string RouteFallbackGroup { get; set; } = "chat";

    [BindProperty]
    public decimal RouteInputPricePerMillion { get; set; }

    [BindProperty]
    public decimal RouteOutputPricePerMillion { get; set; }

    [BindProperty]
    public int RouteSortOrder { get; set; }

    [BindProperty]
    public bool RouteEnabled { get; set; } = true;

    public decimal RemainingBudgetUsd { get; private set; }
    public decimal UsedBudgetUsd { get; private set; }
    public int BudgetUsedPercent { get; private set; }
    public IReadOnlyList<GatewayRouteView> Routes { get; private set; } = [];
    public bool HasOpenAiChatPair { get; private set; }
    public string CurlSnippet { get; private set; } = string.Empty;
    public string TypeScriptSnippet { get; private set; } = string.Empty;
    public string PythonSnippet { get; private set; } = string.Empty;
    public string PepEnvSnippet { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }
    public bool EditingRoute => !string.IsNullOrWhiteSpace(RouteId);

    public async Task OnGetAsync(string? edit, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.GatewayRead);
        await LoadAsync(edit, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.GatewayWrite);
        try
        {
            var outcome = await _admin.RunAsync(
                caller,
                "admin.gateway.update_settings",
                "gateway",
                "settings",
                null,
                PermissionCatalog.GatewayWrite,
                ct => _gateway.UpdateSettingsAsync(
                    HttpContext.RequireOrganizationId(),
                    new GatewaySettingsUpdate(
                        Enabled,
                        RoutingMode,
                        MonthlyBudgetUsd,
                        DefaultTokenBudgetClass,
                        ShortMaxOutputTokens,
                        StandardMaxOutputTokens,
                        DeepMaxOutputTokens,
                        HeuristicCheapMaxTokens,
                        CascadeSkipToStrongTokens,
                        CascadeMaxAttempts,
                        CascadeMaxUsd,
                        ExactCacheTtlSeconds,
                        PrefixCacheEnabled,
                        ShadowRouterEnabled),
                    ct),
                cancellationToken);
            Message = outcome.Allowed ? "AI Gateway settings saved." : (outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason);
            IsError = !outcome.Allowed && !outcome.Pending;
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(null, cancellationToken);
        ClearRouteForm();
        return Page();
    }

    public async Task<IActionResult> OnPostUpsertRouteAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.GatewayWrite);
        ResolveProvider();
        var resourceId = string.IsNullOrWhiteSpace(RouteId) ? RouteAlias : RouteId;
        try
        {
            var outcome = await _admin.RunAsync(
                caller,
                "admin.gateway.upsert_route",
                "gateway",
                resourceId,
                null,
                PermissionCatalog.GatewayWrite,
                ct => _gateway.UpsertRouteAsync(
                    HttpContext.RequireOrganizationId(),
                    new GatewayRouteWrite(
                        RouteId,
                        RouteAlias,
                        RouteProvider,
                        RouteModel,
                        RouteTier,
                        RouteRegion,
                        RouteFallbackGroup,
                        RouteInputPricePerMillion,
                        RouteOutputPricePerMillion,
                        RouteSortOrder,
                        RouteEnabled),
                    ct),
                cancellationToken);
            Message = outcome.Allowed
                ? (EditingRoute ? "Catalog route saved." : "Catalog route added.")
                : (outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason);
            IsError = !outcome.Allowed && !outcome.Pending;
            if (outcome.Allowed)
            {
                ClearRouteForm();
            }
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(IsError ? RouteId : null, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostApplyOpenAiChatPairAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.GatewayWrite);
        try
        {
            var outcome = await _admin.RunAsync(
                caller,
                "admin.gateway.apply_openai_chat_pair",
                "gateway",
                GatewayRecipes.OpenAiChatPairId,
                null,
                PermissionCatalog.GatewayWrite,
                ct => _gateway.ApplyOpenAiChatPairAsync(HttpContext.RequireOrganizationId(), ct),
                cancellationToken);
            Message = outcome.Allowed
                ? "OpenAI chat pair added (chat.cheap, chat.strong, chat.default)."
                : (outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason);
            IsError = !outcome.Allowed && !outcome.Pending;
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(null, cancellationToken);
        ClearRouteForm();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteRouteAsync(string routeId, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.GatewayWrite);
        try
        {
            var outcome = await _admin.RunAsync(
                caller,
                "admin.gateway.delete_route",
                "gateway",
                routeId,
                null,
                PermissionCatalog.GatewayWrite,
                ct => _gateway.DeleteRouteAsync(HttpContext.RequireOrganizationId(), routeId, ct),
                cancellationToken);
            Message = outcome.Allowed ? "Catalog route removed." : (outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason);
            IsError = !outcome.Allowed && !outcome.Pending;
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(null, cancellationToken);
        return Page();
    }

    private async Task LoadAsync(string? editRouteId, CancellationToken cancellationToken)
    {
        if (HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.AuditRead) == true)
            Activity = (await _queries.ActivityAsync(HttpContext.RequireOrganizationId(), HttpContext.RequireProductApplicationId(), null, "gateway", 1, cancellationToken)).Items;
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.GatewayWrite) == true;
        var settings = await _gateway.GetSettingsAsync(HttpContext.RequireOrganizationId(), cancellationToken);
        Enabled = settings.Enabled;
        RoutingMode = settings.RoutingMode;
        MonthlyBudgetUsd = settings.MonthlyBudgetUsd;
        DefaultTokenBudgetClass = settings.DefaultTokenBudgetClass;
        ShortMaxOutputTokens = settings.ShortMaxOutputTokens;
        StandardMaxOutputTokens = settings.StandardMaxOutputTokens;
        DeepMaxOutputTokens = settings.DeepMaxOutputTokens;
        HeuristicCheapMaxTokens = settings.HeuristicCheapMaxTokens;
        CascadeSkipToStrongTokens = settings.CascadeSkipToStrongTokens;
        CascadeMaxAttempts = settings.CascadeMaxAttempts;
        CascadeMaxUsd = settings.CascadeMaxUsd;
        ExactCacheTtlSeconds = settings.ExactCacheTtlSeconds;
        PrefixCacheEnabled = settings.PrefixCacheEnabled;
        ShadowRouterEnabled = settings.ShadowRouterEnabled;
        RemainingBudgetUsd = settings.RemainingBudgetUsd;
        UsedBudgetUsd = settings.UsedBudgetUsd;
        BudgetUsedPercent = settings.MonthlyBudgetUsd <= 0
            ? 0
            : (int)Math.Clamp(decimal.Round(settings.UsedBudgetUsd / settings.MonthlyBudgetUsd * 100m, 0), 0, 100);
        Routes = settings.Routes;
        HasOpenAiChatPair = GatewayRecipes.HasOpenAiChatPair(settings.Routes);
        if (!string.IsNullOrWhiteSpace(editRouteId))
        {
            var route = settings.Routes.FirstOrDefault(item => string.Equals(item.Id, editRouteId, StringComparison.Ordinal));
            if (route is not null)
            {
                PrefillRoute(route);
            }
        }

        CurlSnippet =
            $"curl {PepBaseUrl}/chat/completions \\\n" +
            "  -H \"Authorization: Bearer dgk_YOUR_AGENT_KEY\" \\\n" +
            "  -H \"Content-Type: application/json\" \\\n" +
            "  -d '{\"model\":\"chat.default\",\"messages\":[{\"role\":\"user\",\"content\":\"Hello\"}]}'";
        TypeScriptSnippet =
            "import OpenAI from \"openai\";\n\n" +
            "const client = new OpenAI({\n" +
            "  apiKey: process.env.DELEGUARD_API_KEY,\n" +
            $"  baseURL: \"{PepBaseUrl}\",\n" +
            "});\n\n" +
            "const response = await client.chat.completions.create({\n" +
            "  model: \"chat.default\",\n" +
            "  messages: [{ role: \"user\", content: \"Hello\" }],\n" +
            "});";
        PythonSnippet =
            "from openai import OpenAI\n\n" +
            $"client = OpenAI(api_key=\"dgk_YOUR_AGENT_KEY\", base_url=\"{PepBaseUrl}\")\n" +
            "print(client.chat.completions.create(\n" +
            "    model=\"chat.default\",\n" +
            "    messages=[{\"role\": \"user\", \"content\": \"Hello\"}],\n" +
            "))";
        PepEnvSnippet =
            "Deleguard__Url=http://127.0.0.1:5080\n" +
            "# Optional fallback when the agent does not send Authorization:\n" +
            "# Deleguard__ApiKey=dgk_YOUR_AGENT_KEY\n" +
            "Providers__openai__ApiKey=sk-your-provider-key";
    }

    private void PrefillRoute(GatewayRouteView route)
    {
        RouteId = route.Id;
        RouteAlias = route.Alias;
        RouteProvider = route.Provider;
        RouteProviderPreset = KnownProvider(route.Provider) ? route.Provider : "custom";
        RouteModel = route.Model;
        RouteTier = route.Tier;
        RouteRegion = route.Region;
        RouteFallbackGroup = route.FallbackGroup;
        RouteInputPricePerMillion = route.InputPricePerMillion;
        RouteOutputPricePerMillion = route.OutputPricePerMillion;
        RouteSortOrder = route.SortOrder;
        RouteEnabled = route.Enabled;
    }

    private void ResolveProvider()
    {
        if (!string.Equals(RouteProviderPreset, "custom", StringComparison.OrdinalIgnoreCase))
        {
            RouteProvider = RouteProviderPreset;
        }
    }

    private static bool KnownProvider(string provider) =>
        provider is "openai" or "azure" or "anthropic";

    private void ClearRouteForm()
    {
        RouteId = null;
        RouteAlias = string.Empty;
        RouteProviderPreset = "openai";
        RouteProvider = "openai";
        RouteModel = string.Empty;
        RouteTier = GatewayModelTiers.Cheap;
        RouteRegion = "global";
        RouteFallbackGroup = "chat";
        RouteInputPricePerMillion = 0;
        RouteOutputPricePerMillion = 0;
        RouteSortOrder = 0;
        RouteEnabled = true;
    }
}

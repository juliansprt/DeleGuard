using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Deleguard.AiGateway;

public sealed class DeleguardControlPlaneClient
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly AsyncLocal<string?> RequestApiKey = new();

    private readonly HttpClient _http;
    private readonly GatewayOptions _options;

    public DeleguardControlPlaneClient(HttpClient http, IOptions<GatewayOptions> options)
    {
        _http = http;
        _options = options.Value;
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.Url))
        {
            _http.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
        }
    }

    public IDisposable UseCaller(string? inboundBearer)
    {
        var previous = RequestApiKey.Value;
        RequestApiKey.Value = inboundBearer;
        return new ResetCaller(() => RequestApiKey.Value = previous);
    }

    public async Task<GatewayConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "v1/gateway/config", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GatewayPdpUnavailableException($"Gateway config failed with HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<GatewayConfigSnapshot>(Json, cancellationToken)
               ?? throw new GatewayPdpUnavailableException("Empty gateway config.");
    }

    public async Task<PdpDecision> AuthorizeAsync(
        string action,
        string resourceType,
        string resourceId,
        Dictionary<string, JsonElement> attributes,
        Dictionary<string, JsonElement> context,
        string idempotencyKey,
        string environment,
        CancellationToken cancellationToken,
        string? agentId = null)
    {
        var resolvedAgent = string.IsNullOrWhiteSpace(agentId) ? _options.AgentId : agentId;
        var payload = new
        {
            agent = new { id = resolvedAgent, environment },
            actor = string.IsNullOrWhiteSpace(_options.ActorId) ? null : new { id = _options.ActorId, type = "service" },
            operation = new { id = idempotencyKey, hash = context.GetValueOrDefault("operation_hash").GetString() },
            action,
            resource = new { type = resourceType, id = resourceId, attributes },
            context
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            "v1/authorize",
            payload,
            cancellationToken,
            ("Idempotency-Key", idempotencyKey));
        if (!response.IsSuccessStatusCode)
        {
            throw new GatewayPdpUnavailableException($"PDP failed closed with HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<PdpDecision>(Json, cancellationToken)
               ?? throw new GatewayPdpUnavailableException("Empty PDP decision.");
    }

    public async Task<PdpDecision?> FindOperationAsync(string operationId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "v1/operations/" + Uri.EscapeDataString(operationId), null, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new GatewayPdpUnavailableException("Operation lookup unavailable; refusing a new dispatch.");
        return await response.Content.ReadFromJsonAsync<PdpDecision>(Json, ct);
    }

    public async Task<PdpDecision> GetDecisionAsync(string decisionId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "v1/decisions/" + Uri.EscapeDataString(decisionId), null, ct);
        if (!response.IsSuccessStatusCode) throw new GatewayPdpUnavailableException("Decision retrieval failed closed.");
        return await response.Content.ReadFromJsonAsync<PdpDecision>(Json, ct) ?? throw new GatewayPdpUnavailableException("Empty decision.");
    }

    public async Task ConsumeAsync(PdpDecision decision, CancellationToken ct)
    {
        if (decision.PermitId is null || decision.Operation is null || decision.ResourceSnapshotHash is null)
            throw new GatewayPdpUnavailableException("Execution-bound permit required.");
        using var response = await SendAsync(HttpMethod.Post, "v1/permits/" + Uri.EscapeDataString(decision.PermitId) + "/consume",
            new { operation_id = decision.Operation.Id, operation_hash = decision.Operation.Hash, resource_snapshot_hash = decision.ResourceSnapshotHash }, ct);
        if (!response.IsSuccessStatusCode) throw new GatewayPdpUnavailableException("Consumption failed closed.");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        if (!result.TryGetProperty("acquired", out var acquired) || !acquired.GetBoolean())
            throw new GatewayPdpUnavailableException("Already consumed. Inspect evidence; do not replay automatically.");
    }

    public async Task ReserveAsync(string decisionId, decimal estimatedCostUsd, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "v1/gateway/spend/reserve",
            new { decision_id = decisionId, estimated_cost_usd = estimatedCostUsd },
            cancellationToken);
        await EnsureSpendAsync(response, cancellationToken);
    }

    public async Task SettleAsync(string decisionId, decimal actualCostUsd, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "v1/gateway/spend/settle",
            new { decision_id = decisionId, actual_cost_usd = actualCostUsd },
            cancellationToken);
        await EnsureSpendAsync(response, cancellationToken);
    }

    public async Task ReleaseAsync(string decisionId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "v1/gateway/spend/release",
            new { decision_id = decisionId },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GatewayPdpUnavailableException($"Spend release failed with HTTP {(int)response.StatusCode}.");
        }
    }

    public async Task ReportExecutionAsync(
        string decisionId,
        string status,
        string? providerReference,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var decision = await GetDecisionAsync(decisionId, cancellationToken);
        var payload = new
        {
            execution_id = decision.Execution?.ExecutionId,
            resource_snapshot_hash = decision.ResourceSnapshotHash,
            decision_id = decisionId,
            status,
            provider_reference = providerReference,
            metadata
        };
        using var response = await SendAsync(HttpMethod.Post, "v1/executions", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GatewayPdpUnavailableException($"Evidence report failed with HTTP {(int)response.StatusCode}.");
        }
    }

    public static Dictionary<string, JsonElement> MetadataOnlyAttributes(
        bool inCatalog,
        string modelRequested,
        string modelAlias,
        string modelTier,
        string region,
        int estimatedTokens,
        decimal estimatedCostUsd,
        decimal remainingBudgetUsd,
        bool overBudget,
        string tokenBudgetClass,
        string routingMode,
        string messageHash,
        bool promptHasTools = false,
        bool promptWantsJson = false,
        bool promptHasImage = false,
        string routeReason = GatewayRouteReasons.Static)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["in_catalog"] = JsonSerializer.SerializeToElement(inCatalog),
            ["model_requested"] = JsonSerializer.SerializeToElement(modelRequested),
            ["model_alias"] = JsonSerializer.SerializeToElement(modelAlias),
            ["model_tier"] = JsonSerializer.SerializeToElement(modelTier),
            ["region"] = JsonSerializer.SerializeToElement(region),
            ["estimated_tokens"] = JsonSerializer.SerializeToElement(estimatedTokens),
            ["estimated_cost_usd"] = JsonSerializer.SerializeToElement(estimatedCostUsd),
            ["remaining_budget_usd"] = JsonSerializer.SerializeToElement(remainingBudgetUsd),
            ["over_budget"] = JsonSerializer.SerializeToElement(overBudget),
            ["token_budget_class"] = JsonSerializer.SerializeToElement(tokenBudgetClass),
            ["routing_mode"] = JsonSerializer.SerializeToElement(routingMode),
            ["message_hash"] = JsonSerializer.SerializeToElement(messageHash),
            ["prompt_has_tools"] = JsonSerializer.SerializeToElement(promptHasTools),
            ["prompt_wants_json"] = JsonSerializer.SerializeToElement(promptWantsJson),
            ["prompt_has_image"] = JsonSerializer.SerializeToElement(promptHasImage),
            ["route_reason"] = JsonSerializer.SerializeToElement(routeReason)
        };
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] extraHeaders)
    {
        using var message = new HttpRequestMessage(method, path);
        var key = GatewayAuth.Resolve(RequestApiKey.Value, _options.ApiKey);
        if (!string.IsNullOrWhiteSpace(key))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        foreach (var (name, value) in extraHeaders)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        if (body is not null)
        {
            message.Content = JsonContent.Create(body, options: Json);
        }

        return await _http.SendAsync(message, cancellationToken);
    }

    private static async Task EnsureSpendAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new GatewayPdpUnavailableException($"Spend ledger failed with HTTP {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Spend ledger rejected the reservation: {(int)response.StatusCode} {body}");
    }

    private sealed class ResetCaller : IDisposable
    {
        private readonly Action _reset;
        private bool _disposed;

        public ResetCaller(Action reset) => _reset = reset;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _reset();
        }
    }
}

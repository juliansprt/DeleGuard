using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Deleguard.AiGateway;

public sealed class GatewayPipeline
{
    private readonly DeleguardControlPlaneClient _pdp;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly ExactResponseCache _cache;
    private readonly ILogger<GatewayPipeline> _logger;
    private readonly GatewayOptions _options;

    public GatewayPipeline(
        DeleguardControlPlaneClient pdp,
        IHttpClientFactory http,
        IConfiguration configuration,
        ExactResponseCache cache,
        ILogger<GatewayPipeline> logger,
        IOptions<GatewayOptions> options)
    {
        _pdp = pdp;
        _http = http;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public Task<IResult> ChatAsync(JsonNode body, CancellationToken cancellationToken, string? inboundBearer = null, string? operationId = null) =>
        CompleteAsync(body, "llm.complete", "chat", inboundBearer, operationId, cancellationToken);

    public Task<IResult> EmbedAsync(JsonNode body, CancellationToken cancellationToken, string? inboundBearer = null, string? operationId = null) =>
        CompleteAsync(body, "llm.embed", "embed", inboundBearer, operationId, cancellationToken);

    public async Task<IResult> ModelsAsync(CancellationToken cancellationToken, string? inboundBearer = null)
    {
        if (!GatewayAuth.HasCredential(inboundBearer, _options.ApiKey))
        {
            return GatewayHttp.Unauthenticated();
        }

        using var caller = _pdp.UseCaller(inboundBearer);
        var config = await _pdp.GetConfigAsync(cancellationToken);
        var data = config.Routes.Select(route => new
        {
            id = route.Alias,
            @object = "model",
            owned_by = route.Provider
        });
        return GatewayHttp.WithHeaders(Results.Json(new { @object = "list", data }), "ALLOW");
    }

    private async Task<IResult> CompleteAsync(
        JsonNode body,
        string action,
        string defaultGroup,
        string? inboundBearer,
        string? operationId,
        CancellationToken cancellationToken)
    {
        if (!GatewayAuth.HasCredential(inboundBearer, _options.ApiKey))
        {
            return GatewayHttp.Unauthenticated();
        }

        if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 128 || operationId.Any(char.IsControl))
            return Results.Json(new { error = "Idempotency-Key is required. Reuse it for retries; use a new value for a new operation." }, statusCode: 400);
        if (body is not JsonObject requestObject ||
            requestObject["n"] is { } count && (!int.TryParse(count.ToString(), out var choices) || choices != 1) ||
            requestObject["best_of"] is not null || requestObject["audio"] is not null || requestObject["modalities"] is not null)
            return Results.Json(new { error = "Only one text completion per operation is supported by budget accounting." }, statusCode: 400);
        foreach (var field in new[] { "max_tokens", "max_completion_tokens" })
            if (body[field] is { } value && (!int.TryParse(value.ToString(), out var tokens) || tokens < 1))
                return Results.Json(new { error = "Token limits must be positive integers." }, statusCode: 400);
        using var caller = _pdp.UseCaller(inboundBearer);
        GatewayConfigSnapshot config;
        try
        {
            config = await _pdp.GetConfigAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FailClosed(ex.Message);
        }

        if (!config.Enabled)
        {
            return FailClosed("AI Gateway is disabled for this organization.");
        }

        var requested = body["model"] is JsonValue modelValue && modelValue.TryGetValue<string>(out var modelName)
            ? modelName
            : string.Empty;
        var messages = body["messages"] ?? body["input"];
        var stream = ReadBool(body["stream"]);
        if (action == "llm.embed" && (stream || body["max_tokens"] is not null || body["max_completion_tokens"] is not null || body["tools"] is not null))
            return Results.Json(new { error = "Embeddings do not accept chat generation, tools or streaming parameters." }, statusCode: 400);
        var estimatedTokens = TokenEstimator.EstimateTokens(messages);
        // Byte-based reservation includes tools/schema and framing; routing still uses its length heuristic.
        var reservedInputTokens = checked(Encoding.UTF8.GetByteCount(body.ToJsonString()) + 256 +
            64 * ((messages as JsonArray)?.Count ?? 1));
        var messageHash = TokenEstimator.MessageHash(messages);
        var signals = PromptSignals.FromRequest(body, estimatedTokens);
        if (signals.HasImage) return Results.Json(new { error = "Image token pricing is not certified for this budget-controlled version." }, statusCode: 400);
        var catalog = ModelRouter.Find(config, requested);
        var inCatalog = catalog is not null;
        var seed = catalog ?? new GatewayRoute
        {
            Alias = requested,
            Provider = "unknown",
            Model = requested,
            Tier = defaultGroup == "embed" ? "embed" : "cheap",
            Region = "global",
            FallbackGroup = defaultGroup
        };

        var tokenClass = config.DefaultTokenBudgetClass;
        var maxOutput = config.TokenBudgetClasses.FirstOrDefault(item =>
                            string.Equals(item.Name, tokenClass, StringComparison.Ordinal))?.MaxOutputTokens
                        ?? 2048;
        var requestedMax = signals.RequestedMaxTokens;
        var cappedMax = action == "llm.embed" ? 0 : requestedMax is int n ? Math.Min(n, maxOutput) : maxOutput;
        var truncation = requestedMax is int requestedTokens && requestedTokens > maxOutput ? "capped" : "none";

        var allowlist = inCatalog ? ModelRouter.Allowlist(config, seed) : Array.Empty<GatewayRoute>();
        var cheapMax = config.EffectiveHeuristicCheapMaxTokens;
        var skipStrong = config.EffectiveCascadeSkipToStrongTokens;
        var maxAttempts = config.EffectiveCascadeMaxAttempts;
        var wouldRoute = config.ShadowRouterEnabled ? ModelRouter.ShadowWouldRoute(signals, cheapMax) : "off";
        var decisionRoute = inCatalog
            ? ModelRouter.Choose(config.RoutingMode, seed, allowlist, signals, cheapMax, skipStrong)
            : new RouteDecision(seed, GatewayRouteReasons.Static);
        var chosen = decisionRoute.Route;
        var routeReason = decisionRoute.Reason;
        var planned = new List<GatewayRoute> { chosen };
        if (action == "llm.complete" && inCatalog &&
            string.Equals(config.RoutingMode, "cascade", StringComparison.OrdinalIgnoreCase) &&
            maxAttempts >= 2 &&
            string.Equals(chosen.Tier, "cheap", StringComparison.OrdinalIgnoreCase) &&
            !signals.ForceStrong &&
            signals.EstimatedInputTokens <= skipStrong)
        {
            var strong = ModelRouter.FirstTier(allowlist, "strong");
            if (strong is not null && !string.Equals(strong.Model, chosen.Model, StringComparison.OrdinalIgnoreCase))
            {
                var cascadeCost = ModelRouter.EstimateCostUsd(chosen, reservedInputTokens, cappedMax) +
                                  ModelRouter.EstimateCostUsd(strong, reservedInputTokens, cappedMax);
                if (cascadeCost <= config.CascadeMaxUsd)
                {
                    planned.Add(strong);
                }
            }
        }

        var estimatedCost = ModelRouter.EstimateCostUsd(chosen, reservedInputTokens, cappedMax);
        var overBudget = config.RemainingBudgetUsd <= 0 || estimatedCost > config.RemainingBudgetUsd;
        if (action == "llm.complete") GatewayToolFilter.Apply(body, config);
        if (action == "llm.complete" && config.PrefixCacheEnabled)
        {
            body["prompt_cache_key"] = TokenEstimator.PrefixHash(messages);
        }
        else if (body is JsonObject obj)
        {
            obj.Remove("prompt_cache_key");
        }

        var dispatchMax = PromptSignals.DispatchMax(
            signals,
            routeReason,
            config.TokenBudgetClasses,
            cheapMax,
            cappedMax);
        if (action == "llm.complete") ApplyTokenBudget(body, dispatchMax);

        body["model"] = chosen.Model;
        if (stream) body["stream_options"] = new JsonObject { ["include_usage"] = true };
        var attributes = DeleguardControlPlaneClient.MetadataOnlyAttributes(
            inCatalog,
            requested,
            chosen.Alias,
            chosen.Tier,
            chosen.Region,
            estimatedTokens,
            estimatedCost,
            config.RemainingBudgetUsd,
            overBudget,
            tokenClass,
            config.RoutingMode,
            messageHash,
            signals.HasTools,
            signals.WantsJson,
            signals.HasImage,
            routeReason);

        if (attributes.ContainsKey("messages") || attributes.ContainsKey("prompt") || AuthorizeProbe(attributes).Length > 0)
        {
            return FailClosed("Refusing to send prompts to the PDP.");
        }

        if (config.ShadowRouterEnabled)
        {
            _logger.LogInformation(
                "Shadow router would_route={Would} did_route={Did} route_reason={Reason} has_tools={Tools} wants_json={Json} has_image={Image} estimated_tokens={Tokens} mode={Mode}",
                wouldRoute,
                chosen.Tier,
                routeReason,
                signals.HasTools,
                signals.WantsJson,
                signals.HasImage,
                estimatedTokens,
                config.RoutingMode);
        }

        BindRoute(attributes, chosen);
        var stableId = IdempotencyKey(action, operationId, 0);
        PdpDecision decision;
        try
        {
            decision = await _pdp.FindOperationAsync(stableId, cancellationToken) ?? await _pdp.AuthorizeAsync(
                action,
                "model",
                string.IsNullOrWhiteSpace(chosen.Alias) ? requested : chosen.Alias,
                attributes,
                new Dictionary<string, JsonElement>
                {
                    ["environment"] = JsonSerializer.SerializeToElement(config.Environment),
                    ["operation_hash"] = JsonSerializer.SerializeToElement(CacheKey(chosen.Provider + "/" + chosen.Model, body))
                },
                stableId,
                config.Environment,
                cancellationToken,
                config.AgentId);
            decision = await _pdp.GetDecisionAsync(decision.DecisionId, cancellationToken);
            if (decision.Operation?.Hash != CacheKey(chosen.Provider + "/" + chosen.Model, body))
                return Results.Json(new { error = "Operation content or routing changed. Inspect the original operation before creating a new one." }, statusCode: 409);
            if (decision.Execution is not null)
                return Results.Json(new { error = "Operation already consumed; automatic replay is blocked.", decision.DecisionId, decision.Execution }, statusCode: 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FailClosed(ex.Message);
        }

        if (string.Equals(decision.Decision, "DENY", StringComparison.Ordinal))
        {
            return GatewayHttp.WithHeaders(
                Results.Json(DecisionError("deleguard_deny", decision), statusCode: 403),
                "DENY",
                seed.Model,
                routeReason: routeReason);
        }

        if (string.Equals(decision.Decision, "REQUIRE_APPROVAL", StringComparison.Ordinal))
        {
            return GatewayHttp.WithHeaders(
                Results.Json(DecisionError("deleguard_approval", decision), statusCode: 409),
                "REQUIRE_APPROVAL",
                seed.Model,
                routeReason: routeReason);
        }

        if (!string.Equals(decision.Decision, "ALLOW", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(decision.Permit))
        {
            return FailClosed(decision.Reason ?? "PDP did not ALLOW.");
        }

        if (decision.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            return FailClosed("Permit expired.");
        }

        try
        {
            await _pdp.ReserveAsync(decision.DecisionId, estimatedCost, cancellationToken);
        }
        catch (GatewayPdpUnavailableException ex)
        {
            return FailClosed(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return GatewayHttp.WithHeaders(Results.Json(new
            {
                error = new
                {
                    type = "deleguard_deny",
                    message = ex.Message,
                    decision = "DENY",
                    decision_id = decision.DecisionId
                }
            }, statusCode: 403), "DENY", chosen.Model, routeReason: routeReason);
        }

        var cacheKey = CacheKey(GatewayAuth.Resolve(inboundBearer, _options.ApiKey) + "|" + chosen.Provider + "|" + chosen.Model, body);
        var ttl = config.EffectiveExactCacheTtlSeconds;
        var useCache = !stream && body["tools"] is null && ttl > 0 && !config.SemanticCacheEnabled;
        if (useCache && _cache.TryGet(cacheKey, ttl, out var cached))
        {
            await _pdp.ConsumeAsync(decision, cancellationToken);
            await _pdp.SettleAsync(decision.DecisionId, 0, cancellationToken);
            await FinishAsync(decision.DecisionId, "SUCCESS", chosen, requested, true, estimatedTokens, 0, 0, tokenClass, config.RoutingMode, wouldRoute, truncation, messageHash, routeReason, cancellationToken);
            return GatewayHttp.WithHeaders(Results.Content(cached, "application/json"), "ALLOW", chosen.Model, "hit", routeReason);
        }

        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        GatewayRoute? used = null;
        HttpResponseMessage? providerResponse = null;
        string? responseJson = null;
        var cascadeAttempts = 0;
        var current = chosen;
        try
        {
            while (current is not null && cascadeAttempts < maxAttempts)
            {
                if (inCatalog && !allowlist.Any(route => string.Equals(route.Model, current.Model, StringComparison.OrdinalIgnoreCase) && route.Provider == current.Provider && route.Region == current.Region))
                {
                    await _pdp.ReleaseAsync(decision.DecisionId, cancellationToken);
                    return FailClosed("Router picked a model outside the authorized fallback graph.");
                }

                if (!tried.Add(current.Model))
                {
                    break;
                }

                body["model"] = current.Model;
                providerResponse?.Dispose();
                if (cascadeAttempts > 0)
                {
                    var freshConfig = await _pdp.GetConfigAsync(cancellationToken);
                    if (!freshConfig.Enabled || !freshConfig.Routes.Any(r => r.Alias == current.Alias && r.Provider == current.Provider && r.Model == current.Model && r.Region == current.Region))
                        return FailClosed("Provider route revoked during routing.");
                    estimatedCost = ModelRouter.EstimateCostUsd(current, reservedInputTokens, cappedMax);
                    var attemptAttributes = DeleguardControlPlaneClient.MetadataOnlyAttributes(true, requested, current.Alias, current.Tier, current.Region,
                        estimatedTokens, estimatedCost, freshConfig.RemainingBudgetUsd, estimatedCost > freshConfig.RemainingBudgetUsd,
                        tokenClass, config.RoutingMode, messageHash, signals.HasTools, signals.WantsJson, signals.HasImage, routeReason);
                    BindRoute(attemptAttributes, current);
                    decision = await _pdp.AuthorizeAsync(action, "model", current.Alias, attemptAttributes,
                        new() { ["operation_hash"] = JsonSerializer.SerializeToElement(CacheKey(current.Provider + "/" + current.Model, body)) },
                        IdempotencyKey(action, operationId, cascadeAttempts), config.Environment, cancellationToken, config.AgentId);
                    decision = await _pdp.GetDecisionAsync(decision.DecisionId, cancellationToken);
                    if (decision.Decision != "ALLOW" || string.IsNullOrWhiteSpace(decision.Permit))
                        return GatewayHttp.WithHeaders(Results.Json(DecisionError("deleguard_approval_or_deny", decision), statusCode: 409), decision.Decision);
                    await _pdp.ReserveAsync(decision.DecisionId, estimatedCost, cancellationToken);
                }
                await _pdp.ConsumeAsync(decision, cancellationToken);
                providerResponse = await DispatchAsync(current, body, stream, action, cancellationToken);
                used = current;
                if (!providerResponse.IsSuccessStatusCode)
                {
                    if (inCatalog && ModelRouter.ShouldFailover(providerResponse.StatusCode))
                    {
                        // Failure after dispatch may still incur usage. Preserve a conservative estimate for this attempt.
                        await _pdp.ReleaseAsync(decision.DecisionId, cancellationToken);
                        await FinishAsync(decision.DecisionId, "UNKNOWN", current, requested, false, estimatedTokens, 0, estimatedCost, tokenClass, config.RoutingMode, wouldRoute, truncation, messageHash, routeReason, cancellationToken);
                        cascadeAttempts++;
                        current = ModelRouter.NextFailover(allowlist, tried);
                        continue;
                    }

                    break;
                }

                if (stream)
                {
                    break;
                }

                responseJson = await providerResponse.Content.ReadAsStringAsync(cancellationToken);
                var escalate = action == "llm.embed" ? null : CascadeScorer.EscalateReason(
                    config.RoutingMode,
                    body,
                    responseJson,
                    cascadeAttempts,
                    planned.Count);
                if (escalate is not null)
                {
                    cascadeAttempts++;
                    var nextStrong = ModelRouter.FirstTier(allowlist, "strong");
                    if (nextStrong is not null && !tried.Contains(nextStrong.Model))
                    {
                        var priorUsage = ReadUsage(responseJson);
                        if (priorUsage.Input > 0) await _pdp.SettleAsync(decision.DecisionId, ModelRouter.EstimateCostUsd(current, priorUsage.Input, priorUsage.Output), cancellationToken);
                        else await _pdp.ReleaseAsync(decision.DecisionId, cancellationToken);
                        await FinishAsync(decision.DecisionId, "SUCCESS", current, requested, false, priorUsage.Input, priorUsage.Output, estimatedCost, tokenClass, config.RoutingMode, wouldRoute, truncation, messageHash, routeReason, cancellationToken);
                        routeReason = GatewayRouteReasons.CascadeEscalate;
                        ApplyTokenBudget(body, cappedMax);
                        current = nextStrong;
                        continue;
                    }
                }

                break;
            }

            if (providerResponse is null || used is null)
            {
                await _pdp.ReleaseAsync(decision.DecisionId, cancellationToken);
                return FailClosed("No authorized provider remained after failover.");
            }

            if (!providerResponse.IsSuccessStatusCode)
            {
                await _pdp.ReleaseAsync(decision.DecisionId, cancellationToken);
                await FinishAsync(decision.DecisionId, "FAILED", used, requested, false, estimatedTokens, 0, 0, tokenClass, config.RoutingMode, wouldRoute, truncation, messageHash, routeReason, cancellationToken);
                return GatewayHttp.WithHeaders(Results.StatusCode((int)providerResponse.StatusCode), "ALLOW", used.Model, routeReason: routeReason);
            }

            if (stream)
            {
                var streamed = providerResponse;
                providerResponse = null;
                var streamDecisionId = decision.DecisionId;
                var streamRoute = used;
                var streamEstimate = estimatedCost;
                return GatewayHttp.WithHeaders(Results.Stream(async output =>
                {
                    using var streamCaller = _pdp.UseCaller(inboundBearer);
                    try
                    {
                        StreamReceipt receipt;
                        using (streamed)
                            receipt = await ProviderStreamCopy.CopyAsync(await streamed.Content.ReadAsStreamAsync(cancellationToken), output, cancellationToken);
                        var cost = streamEstimate;
                        if (receipt.Complete && receipt.InputTokens is { } input && receipt.OutputTokens is { } generated)
                        {
                            cost = ModelRouter.EstimateCostUsd(streamRoute, input, generated);
                            await _pdp.SettleAsync(streamDecisionId, cost, CancellationToken.None);
                        }
                        else await _pdp.ReleaseAsync(streamDecisionId, CancellationToken.None);
                        await FinishAsync(streamDecisionId, receipt.Complete ? "SUCCESS" : "UNKNOWN", streamRoute, requested, false,
                            receipt.InputTokens ?? 0, receipt.OutputTokens ?? 0, cost, tokenClass, config.RoutingMode, wouldRoute, truncation, messageHash, routeReason, CancellationToken.None);
                    }
                    catch
                    {
                        try { await _pdp.ReleaseAsync(streamDecisionId, CancellationToken.None); } catch { /* Reservation remains reserved. */ }
                        throw;
                    }
                }, "text/event-stream"), "ALLOW", used.Model, routeReason: routeReason);
            }

            var json = responseJson ?? await providerResponse.Content.ReadAsStringAsync(cancellationToken);
            var (inputTokens, outputTokens) = ReadUsage(json);
            var actualCost = ModelRouter.EstimateCostUsd(used, inputTokens == 0 ? estimatedTokens : inputTokens, outputTokens);
            if (useCache)
            {
                _cache.Set(cacheKey, json);
            }

            if (inputTokens > 0) await _pdp.SettleAsync(decision.DecisionId, actualCost, cancellationToken);
            else await _pdp.ReleaseAsync(decision.DecisionId, cancellationToken);
            await FinishAsync(decision.DecisionId, "SUCCESS", used, requested, false, inputTokens, outputTokens, actualCost, tokenClass, config.RoutingMode, wouldRoute, truncation, messageHash, routeReason, cancellationToken);
            return GatewayHttp.WithHeaders(Results.Content(json, "application/json"), "ALLOW", used.Model, routeReason: routeReason);
        }
        catch (Exception ex)
        {
            try { await _pdp.ReleaseAsync(decision.DecisionId, CancellationToken.None); } catch { /* Keep reservation when accounting is unavailable. */ }
            return FailClosed(ex.Message);
        }
        finally
        {
            providerResponse?.Dispose();
        }
    }

    public static string[] AuthorizeProbe(Dictionary<string, JsonElement> attributes) =>
        attributes.Keys.Where(key => key.Contains("message", StringComparison.OrdinalIgnoreCase) && key != "message_hash"
                                     || string.Equals(key, "prompt", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(key, "messages", StringComparison.OrdinalIgnoreCase)).ToArray();

    private async Task<HttpResponseMessage> DispatchAsync(GatewayRoute route, JsonNode body, bool stream, string action, CancellationToken cancellationToken)
    {
        var section = _configuration.GetSection($"Providers:{route.Provider}");
        var baseUrl = section["BaseUrl"];
        var apiKey = section["ApiKey"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GatewayPdpUnavailableException($"Provider '{route.Provider}' is not configured on the PEP.");
        }

        var client = _http.CreateClient("provider");
        var path = string.Equals(action, "llm.embed", StringComparison.Ordinal) ? "embeddings" : "chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, path))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await client.SendAsync(request, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    private static void ApplyTokenBudget(JsonNode body, int cappedMax)
    {
        if (body is JsonObject obj)
        {
            if (obj.ContainsKey("max_completion_tokens"))
            {
                obj.Remove("max_tokens");
                obj["max_completion_tokens"] = cappedMax;
            }
            else obj["max_tokens"] = cappedMax;
        }
    }

    private async Task FinishAsync(
        string decisionId,
        string status,
        GatewayRoute used,
        string requested,
        bool cacheHit,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        string tokenClass,
        string routingMode,
        string wouldRoute,
        string truncation,
        string messageHash,
        string routeReason,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "ai-gateway",
            ["model_requested"] = requested,
            ["model_used"] = used.Model,
            ["cache_hit"] = cacheHit ? "true" : "false",
            ["input_tokens"] = inputTokens.ToString(),
            ["output_tokens"] = outputTokens.ToString(),
            ["cost_usd"] = costUsd.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["token_budget_class"] = tokenClass,
            ["routing_mode"] = routingMode,
            ["would_route"] = wouldRoute,
            ["truncation_status"] = truncation,
            ["message_hash"] = messageHash,
            ["route_reason"] = routeReason
        };
        await _pdp.ReportExecutionAsync(decisionId, status, used.Provider + "/" + used.Model, metadata, cancellationToken);
    }

    private static (int Input, int Output) ReadUsage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("usage", out var usage))
            {
                return (0, 0);
            }

            var input = usage.TryGetProperty("prompt_tokens", out var prompt) ? prompt.GetInt32() : 0;
            var output = usage.TryGetProperty("completion_tokens", out var completion) ? completion.GetInt32() : 0;
            return (input, output);
        }
        catch (JsonException)
        {
            return (0, 0);
        }
    }

    private static string CacheKey(string model, JsonNode body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(model + "|" + Canonical(body).ToJsonString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static JsonNode Canonical(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new KeyValuePair<string, JsonNode?>(x.Key, x.Value is null ? null : Canonical(x.Value)))),
        JsonArray array => new JsonArray(array.Select(x => x is null ? null : Canonical(x)).ToArray()),
        _ => node.DeepClone()
    };

    private static string IdempotencyKey(string action, string operationId, int attempt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{action}|{operationId}|{attempt}"));
        return "gtw_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void BindRoute(Dictionary<string, JsonElement> attributes, GatewayRoute route)
    {
        attributes["provider"] = JsonSerializer.SerializeToElement(route.Provider);
        attributes["provider_model"] = JsonSerializer.SerializeToElement(route.Model);
        attributes["input_price_per_million"] = JsonSerializer.SerializeToElement(route.InputPricePerMillion);
        attributes["output_price_per_million"] = JsonSerializer.SerializeToElement(route.OutputPricePerMillion);
    }

    private static string Combine(string baseUrl, string path) =>
        baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    private static IResult FailClosed(string reason) =>
        GatewayHttp.WithHeaders(
            Results.Json(new { error = new { type = "deleguard_unavailable", message = reason } }, statusCode: 503),
            "UNAVAILABLE");

    private static bool ReadBool(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<bool>(out var flag))
        {
            return flag;
        }

        return false;
    }

    private static object DecisionError(string type, PdpDecision decision) => new
    {
        error = new
        {
            type,
            message = decision.Reason ?? decision.Decision,
            decision = decision.Decision,
            decision_id = decision.DecisionId,
            approval_id = decision.ApprovalId
        }
    };
}

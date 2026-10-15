using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Deleguard.McpGuard;

public sealed class DeleguardPdpClient : IDeleguardPdp, IGuardConfigSource
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly GuardOptions _options;
    private GuardConfigSnapshot? _cached;
    private DateTimeOffset _cachedAt;

    public DeleguardPdpClient(HttpClient http, IOptions<GuardOptions> options)
    {
        _http = http;
        _options = options.Value;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
        }

        if (_http.DefaultRequestHeaders.Authorization is null && !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public Task<GuardConfigSnapshot> GetAsync(CancellationToken cancellationToken) => GetConfigAsync(cancellationToken);

    public Task<GuardConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(30))
        {
            return Task.FromResult(_cached);
        }

        return GetFreshAsync(cancellationToken);
    }

    public async Task<GuardConfigSnapshot> GetFreshAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("v1/guard/config", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GuardPdpUnavailableException($"Guard config failed with HTTP {(int)response.StatusCode}.");
        }

        var parsed = await response.Content.ReadFromJsonAsync<GuardConfigSnapshot>(Json, cancellationToken)
                     ?? throw new GuardPdpUnavailableException("Empty guard config.");
        _cached = parsed;
        _cachedAt = DateTimeOffset.UtcNow;
        return parsed;
    }

    public async Task<PdpDecision> AuthorizeAsync(
        string action,
        string resourceType,
        string resourceId,
        Dictionary<string, JsonElement> attributes,
        Dictionary<string, string> context,
        string idempotencyKey,
        string environment,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            agent = new { id = _options.AgentId, environment },
            actor = string.IsNullOrWhiteSpace(_options.ActorId) ? null : new { id = _options.ActorId, type = "service" },
            operation = new { id = idempotencyKey, hash = context.GetValueOrDefault("operation_hash") },
            action,
            resource = new
            {
                type = resourceType,
                id = resourceId,
                attributes
            },
            context
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/authorize")
        {
            Content = JsonContent.Create(payload, options: Json)
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var response = await _http.SendAsync(message, cancellationToken);
        return await ReadDecisionAsync(response, cancellationToken);
    }

    public async Task<PdpDecision> GetDecisionAsync(string decisionId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"v1/decisions/{Uri.EscapeDataString(decisionId)}", cancellationToken);
        return await ReadDecisionAsync(response, cancellationToken);
    }

    public async Task<bool> ConsumeAsync(PdpDecision decision, CancellationToken cancellationToken)
    {
        if (decision.PermitId is null || decision.Operation is null || decision.ResourceSnapshotHash is null) return false;
        using var response = await _http.PostAsJsonAsync("v1/permits/" + Uri.EscapeDataString(decision.PermitId) + "/consume", new
        { operation_id = decision.Operation.Id, operation_hash = decision.Operation.Hash, resource_snapshot_hash = decision.ResourceSnapshotHash }, Json, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new GuardPdpUnavailableException("Permit consumption failed closed.");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);
        return result.TryGetProperty("acquired", out var acquired) && acquired.GetBoolean();
    }

    public async Task ReportExecutionAsync(
        string decisionId,
        string status,
        string? providerReference,
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
            metadata = new Dictionary<string, string> { ["source"] = "mcp-guard" }
        };
        using var response = await _http.PostAsJsonAsync("v1/executions", payload, Json, cancellationToken);
        if ((int)response.StatusCode >= 500)
        {
            throw new GuardPdpUnavailableException($"Evidence report failed with HTTP {(int)response.StatusCode}.");
        }

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Evidence report failed: {(int)response.StatusCode} {body}");
        }
    }

    public static string IdempotencyKey(string server, string tool, JsonElement args)
    {
        var canonical = CanonicalJson(args);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{server}|{tool}|{canonical}"));
        return "grd_" + Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    private static async Task<PdpDecision> ReadDecisionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new GuardPdpUnavailableException($"PDP failed closed with HTTP {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<PdpDecision>(body, Json)
                   ?? throw new GuardPdpUnavailableException("Empty PDP decision.");
        }
        catch (JsonException ex)
        {
            throw new GuardPdpUnavailableException($"Invalid PDP decision: {ex.Message}");
        }
    }

    internal static string CanonicalJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, element);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

public sealed class GuardPdpUnavailableException : Exception
{
    public GuardPdpUnavailableException(string message) : base(message)
    {
    }
}

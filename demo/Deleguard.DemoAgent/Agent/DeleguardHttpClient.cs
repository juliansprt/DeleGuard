using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Deleguard.DemoAgent.Agent;

public sealed class DeleguardHttpClient : IDeleguardClient
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly DeleguardOptions _options;

    public DeleguardHttpClient(HttpClient http, IOptions<DeleguardOptions> options)
    {
        _http = http;
        _options = options.Value;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
        }

        if (_http.DefaultRequestHeaders.Authorization is null && !string.IsNullOrWhiteSpace(_options.AgentKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AgentKey);
        }
    }

    public async Task<DeleguardDecision> AuthorizeAsync(DeleguardAuthorizeRequest request, CancellationToken cancellationToken)
    {
        var attributes = request.Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);
        var payload = new
        {
            Agent = new { Id = _options.AgentId, Environment = _options.Environment },
            Actor = new { Id = _options.AgentId, Type = "service" },
            Action = request.Action,
            Resource = new
            {
                Type = "invoice",
                Id = request.ResourceId,
                Attributes = attributes
            },
            Context = new Dictionary<string, string> { ["source"] = "demo-agent" }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/authorize")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await _http.SendAsync(message, cancellationToken);
        return await ReadDecisionAsync(response, cancellationToken);
    }

    public async Task<DeleguardDecision> GetDecisionAsync(string decisionId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"v1/decisions/{Uri.EscapeDataString(decisionId)}", cancellationToken);
        return await ReadDecisionAsync(response, cancellationToken);
    }

    public async Task ReportExecutionAsync(string decisionId, string? permit, string invoiceId, CancellationToken cancellationToken)
    {
        _ = permit;
        var payload = new
        {
            DecisionId = decisionId,
            Status = "SUCCESS",
            ProviderReference = invoiceId,
            Metadata = new Dictionary<string, bool> { ["demo"] = true }
        };
        using var response = await _http.PostAsync(
            "v1/executions",
            new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task<DeleguardDecision> ReadDecisionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await EnsureSuccessAsync(response, cancellationToken);
        var parsed = JsonSerializer.Deserialize<DeleguardDecisionDto>(body, Json)
                     ?? throw new DemoDeleguardException(response.StatusCode, "Empty decision payload.");
        return parsed.ToDecision();
    }

    private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        throw new DemoDeleguardException(response.StatusCode, string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "Deleguard error" : body);
    }

    private sealed class DeleguardDecisionDto
    {
        public string Decision { get; set; } = string.Empty;
        public string DecisionId { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? Permit { get; set; }
        public string? ApprovalId { get; set; }
        public string? PolicyId { get; set; }
        public int? PolicyVersion { get; set; }

        public DeleguardDecision ToDecision() =>
            new(Decision, DecisionId, Reason, Permit, ApprovalId, PolicyId, PolicyVersion);
    }
}

public sealed class DemoDeleguardException : Exception
{
    public DemoDeleguardException(System.Net.HttpStatusCode statusCode, string body)
        : base($"Deleguard HTTP {(int)statusCode}: {body}")
    {
        StatusCode = statusCode;
        Body = body;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
    public string Body { get; }
}

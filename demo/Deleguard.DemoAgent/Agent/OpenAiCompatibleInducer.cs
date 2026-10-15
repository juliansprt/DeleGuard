using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Deleguard.DemoAgent.Agent;

public sealed class OpenAiCompatibleInducer : IIntentInducer
{
    private static readonly Regex FacId = new(@"^FAC-[A-Z0-9][A-Z0-9_-]{0,26}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpFactory;
    private readonly LlmOptions _options;
    private readonly IDemoInvoiceStore _store;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public OpenAiCompatibleInducer(
        IHttpClientFactory httpFactory,
        IOptions<LlmOptions> options,
        IDemoInvoiceStore store)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _store = store;
    }

    public static string BuildSystemPrompt(IReadOnlyList<string> invoiceIds)
    {
        var ids = invoiceIds.Count == 0
            ? "(none — the invoice table is empty)"
            : string.Join(", ", invoiceIds);
        return
            """
            You extract invoice-agent intent. Reply with a JSON object (the word json matters) with keys action and resource_id.
            action is invoice.cancel when the user wants to cancel, anular, void, eliminar, delete, borrar, or remove an invoice; otherwise unknown.
            resource_id must be copied exactly from this live table of invoice ids:
            """
            + $" {ids}."
            + """

            If the user writes an id such as FAC-01, use that id when it is in the list. Nicknames (draft, high value, tax/DIAN) map only to a listed id.
            If several invoices are implied, pick the one named; if none match, resource_id is null.
            Do not decide ALLOW or DENY. Do not invent business facts. Extra keys such as amount or status will be ignored.
            """;
    }

    public async Task<InducedIntent> InduceAsync(string userText, CancellationToken cancellationToken)
    {
        var client = _httpFactory.CreateClient("llm");
        var payload = new
        {
            model = _options.Model,
            temperature = 0,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = BuildSystemPrompt(_store.List().Select(invoice => invoice.Id).ToList()) },
                new { role = "user", content = userText }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine("chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LLM HTTP {(int)response.StatusCode}: {Trim(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return ParseModelJson(content);
    }

    public static InducedIntent ParseModelJson(string content, string source = "llm")
    {
        var json = UnwrapJson(content);
        using var intentDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = intentDoc.RootElement;
        var action = NormalizeAction(ReadString(root, "action"));
        var resourceId = NormalizeId(ReadString(root, "resource_id") ?? ReadString(root, "resourceId"));
        var ignored = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("action") ||
                property.NameEquals("resource_id") ||
                property.NameEquals("resourceId"))
            {
                continue;
            }

            ignored[property.Name] = property.Value.Clone();
        }

        return new InducedIntent(action, resourceId, json, ignored, source);
    }

    internal static string UnwrapJson(string content)
    {
        var text = (content ?? string.Empty).Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstNl = text.IndexOf('\n');
        if (firstNl >= 0)
        {
            text = text[(firstNl + 1)..];
        }

        var fence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            text = text[..fence];
        }

        return text.Trim();
    }

    private static string NormalizeAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "unknown";
        }

        var value = action.Trim();
        if (value.Equals("invoice.cancel", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("cancela", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("anular", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("eliminar", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("delete", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("borrar", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("remove", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("void", StringComparison.OrdinalIgnoreCase))
        {
            return "invoice.cancel";
        }

        return value;
    }

    private string Combine(string relative)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/{relative.TrimStart('/')}";
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NormalizeId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        return FacId.IsMatch(trimmed) ? trimmed.ToUpperInvariant() : trimmed;
    }

    private static string Trim(string body) => body.Length <= 300 ? body : body[..300];
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;

var baseUrl = Environment.GetEnvironmentVariable("DELEGUARD_URL") ?? "http://localhost:8080";
var token = Environment.GetEnvironmentVariable("DELEGUARD_TOKEN") ?? "dgk_demo_invoice_agent_local_dev_only";
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

await PrintDecision("FAC-DRAFT", "draft", 5_000_000, false);
await PrintDecision("FAC-8392", "issued", 35_000_000, false);
await PrintDecision("FAC-TAX", "issued", 1_000, true);

async Task PrintDecision(string id, string status, long amount, bool reported)
{
    // Decision-only demo. Persist this identity for retries; consume before any real action.
    var operationId = Guid.NewGuid().ToString("N");
    var operationHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { action = "invoice.cancel", amount, id, reported, status }))).ToLowerInvariant();
    using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
    {
        Content = JsonContent.Create(new
        {
            operation = new { id = operationId, hash = operationHash },
            agent = new { id = "invoice-agent", environment = "production" },
            actor = new { id = "usr_481", type = "human" },
            action = "invoice.cancel",
            resource = new
            {
                type = "invoice",
                id,
                attributes = new { amount, currency = "COP", status, reportedToTaxAuthority = reported }
            },
            context = new { reason = "demo" }
        })
    };
    request.Headers.Add("Idempotency-Key", operationId);
    var response = await http.SendAsync(request);
    var json = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"{id} {response.StatusCode} {json}");
}

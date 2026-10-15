// Minimal conceptual example - adapt authentication/DTOs to the generated SDK.
using System.Net.Http.Json;

var http = new HttpClient { BaseAddress = new Uri("https://api.deleguard.example") };
http.DefaultRequestHeaders.Authorization =
    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("DELEGUARD_TOKEN"));

var request = new {
    agent = new { id = "invoice-agent", environment = "production" },
    actor = new { id = "usr_481", type = "human" },
    action = "invoice.cancel",
    resource = new {
        type = "invoice",
        id = "FAC-8392",
        attributes = new { amount = 35_000_000, currency = "COP", status = "issued" }
    },
    context = new { reason = "duplicate invoice" }
};

using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize") {
    Content = JsonContent.Create(request)
};
message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

var response = await http.SendAsync(message);
response.EnsureSuccessStatusCode();
var decision = await response.Content.ReadFromJsonAsync<Decision>();

if (decision?.decision == "ALLOW") {
    // await existingInvoiceService.CancelAsync("FAC-8392");
}

public record Decision(string decision, string decision_id, string? permit, string? approval_id, string? reason);

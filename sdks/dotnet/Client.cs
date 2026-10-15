using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Deleguard.Client;
public sealed class UncertainOperationException(string message) : Exception(message);
public sealed class Client(HttpClient http)
{
    // Configure BaseAddress and the dgk_ bearer on a dedicated HttpClient. Do not install POST retry handlers.
    private async Task<JsonObject?> RequestAsync(string path, JsonObject? body, CancellationToken ct, string? key = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct); return text.Length == 0 ? null : JsonNode.Parse(text)!.AsObject();
    }
    public Task<JsonObject?> AuthorizeAsync(JsonObject request, CancellationToken ct = default) =>
        RequestAsync("v1/authorize", request, ct, "sdk_" + request["operation"]!["id"]!.GetValue<string>());
    public async Task<JsonObject> WaitAsync(JsonObject decision, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            decision = (await RequestAsync("v1/decisions/" + Uri.EscapeDataString(decision["decision_id"]!.GetValue<string>()), null, ct))!;
            if (decision["decision"]!.GetValue<string>() != "REQUIRE_APPROVAL") return decision;
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Approval pending; retain operation identity.");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
    public async Task<T> ExecuteAsync<T>(JsonObject request, Func<CancellationToken, Task<T>> execute,
        Func<CancellationToken, Task<string>> currentSnapshot, CancellationToken ct = default)
    {
        var d = await WaitAsync((await AuthorizeAsync(request, ct))!, TimeSpan.FromMinutes(15), ct);
        if (d["decision"]!.GetValue<string>() != "ALLOW" || d["permit"] is null) throw new UnauthorizedAccessException("No live ALLOW permit.");
        var snapshot = await currentSnapshot(ct);
        if (snapshot != d["resource_snapshot_hash"]!.GetValue<string>()) throw new InvalidOperationException("Resource changed; reauthorize.");
        var claim = (await RequestAsync("v1/permits/" + Uri.EscapeDataString(d["permit_id"]!.GetValue<string>()) + "/consume",
            new JsonObject { ["operation_id"] = request["operation"]!["id"]!.DeepClone(), ["operation_hash"] = request["operation"]!["hash"]!.DeepClone(), ["resource_snapshot_hash"] = snapshot }, ct))!;
        if (!claim["acquired"]!.GetValue<bool>()) throw new UncertainOperationException("Already consumed: " + claim["execution_id"]);
        var receipt = new JsonObject { ["decision_id"] = d["decision_id"]!.DeepClone(), ["permit_id"] = d["permit_id"]!.DeepClone(),
            ["execution_id"] = claim["execution_id"]!.DeepClone(), ["resource_snapshot_hash"] = snapshot };
        T value;
        try { value = await execute(ct); }
        catch { receipt["status"] = "UNKNOWN"; try { await RequestAsync("v1/executions", receipt, CancellationToken.None); } catch { } throw; }
        receipt["status"] = "SUCCESS";
        try { await RequestAsync("v1/executions", receipt, CancellationToken.None); }
        catch { throw new UncertainOperationException("Executed; evidence delivery uncertain: " + claim["execution_id"]); }
        return value;
    }
}

using System.Net.Http.Json;
using System.Text.Json;
namespace Deleguard.IntegrationTests;
internal static class ExecutionContract
{
    public static async Task<string> ConsumeAsync(HttpClient client, JsonElement decision)
    {
        var operation = decision.GetProperty("operation");
        var response = await client.PostAsJsonAsync("/v1/permits/" + decision.GetProperty("permit_id").GetString() + "/consume",
            new { operation_id = operation.GetProperty("id").GetString(), operation_hash = operation.GetProperty("hash").GetString(),
                resource_snapshot_hash = decision.GetProperty("resource_snapshot_hash").GetString() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var claim = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(claim.GetProperty("acquired").GetBoolean());
        return claim.GetProperty("execution_id").GetString()!;
    }
}

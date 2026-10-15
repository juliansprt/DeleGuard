using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Deleguard.Domain.Common;
using Deleguard.Application.Authorization;

namespace Microsoft.AspNetCore.Mvc.Testing;

/// <summary>Test callers allocate a new identity for each new command. Explicit retry identities win.</summary>
public sealed class OperationIdentityHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head &&
            request.Content?.Headers.ContentType?.MediaType != "application/x-www-form-urlencoded" &&
            !request.Headers.Contains("Idempotency-Key"))
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        // Fixtures are trusted integrations: new commands have identities, explicit retry identities win.
        if (request.RequestUri?.AbsolutePath == "/v1/authorize" && request.Content is not null)
        {
            var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
            if (body["operation"] is null && !AuthorizeHandler.IsIdeAction(body["action"]?.ToString() ?? ""))
            {
                var hash = CanonicalJson.HashHex(body);
                body["operation"] = new JsonObject {
                    ["id"] = request.Headers.GetValues("Idempotency-Key").Single(), ["hash"] = hash };
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
        }
        return await base.SendAsync(request, ct);
    }
}

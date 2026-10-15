using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Application.Evidence;
using Deleguard.Domain.Common;
using Deleguard.Domain.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Deleguard.Api.Mcp;

public sealed class RemoteGuardOptions
{
    public Dictionary<string, RemoteGuardServerOptions> Servers { get; set; } = new(StringComparer.Ordinal);
}
public sealed class RemoteGuardServerOptions
{
    public string OrganizationId { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? BearerToken { get; set; }
    public string[] AllowedHosts { get; set; } = [];
}
public sealed record RemoteTool(string Name, string? Description, JsonElement InputSchema);

public sealed class RemoteGuardMcpDispatcher(IGuardMappingStore mappings, GuardConfigHandler config,
    AuthorizeHandler authorize, GetDecisionHandler decisions, IExecutionControl execution, ReportExecutionHandler evidence,
    IRemoteCallStore pending, IDataProtectionProvider protection, IHttpClientFactory clients,
    IOptions<RemoteGuardOptions> options, ILogger<RemoteGuardMcpDispatcher> logger)
{
    public async Task<IReadOnlyList<GuardMappingRecord>> ListAsync(AgentPrincipal a, CancellationToken ct)
    {
        var grants = (await config.HandleAsync(a, ct)).Mappings.Select(m => m.BindingId).ToHashSet();
        return (await mappings.ListAsync(a.OrganizationId, ct)).Where(m => grants.Contains(m.BindingId) &&
            options.Value.Servers.TryGetValue(m.McpServer, out var s) && s.OrganizationId == a.OrganizationId && s.ApplicationId == a.ApplicationId).ToList();
    }
    public async Task<IReadOnlyList<RemoteTool>> DiscoverAsync(AgentPrincipal a, CancellationToken ct)
    {
        var result = new List<RemoteTool> { new("get_decision", "Resume the original protected operation after approval.",
            JsonSerializer.SerializeToElement(new { type = "object", required = new[] { "decision_id" }, properties = new { decision_id = new { type = "string" } }, additionalProperties = false })) };
        foreach (var group in (await ListAsync(a, ct)).GroupBy(m => m.McpServer))
        {
            await using var client = await ConnectAsync(options.Value.Servers[group.Key], ct);
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            foreach (var m in group)
            {
                var tool = tools.SingleOrDefault(t => t.Name == m.McpTool);
                if (tool is not null) result.Add(new(group.Key + "__" + tool.Name, tool.Description, OperationSchema.WithIdentity(tool.JsonSchema)));
            }
        }
        return result;
    }
    private IDataProtector Protector(AgentPrincipal a) => protection.CreateProtector("deleguard.remote-pending.v1", a.OrganizationId, a.ApplicationId, a.AgentId);
    private static string BindingHash(GuardMappingRecord m, RemoteGuardServerOptions s) =>
        CanonicalJson.HashHex(JsonSerializer.SerializeToNode(new { m.BindingId, m.Action, m.ResourceType, m.ResourceIdPath, m.AttributePathsJson, s.Url }));

    public async Task<GuardCallResult> CallAsync(AgentPrincipal a, string tool, JsonElement arguments, CancellationToken ct, string? operationId = null)
    {
        if (arguments.TryGetProperty(OperationSchema.Field, out var identity) && identity.ValueKind == JsonValueKind.String)
        {
            operationId ??= identity.GetString();
            var clean = System.Text.Json.Nodes.JsonNode.Parse(arguments.GetRawText())!.AsObject();
            clean.Remove(OperationSchema.Field);
            arguments = JsonSerializer.SerializeToElement(clean);
        }
        PendingRemoteCall? saved = null;
        if (tool == "get_decision")
        {
            var id = ReadPath(arguments, "decision_id");
            if (id is not { ValueKind: JsonValueKind.String }) return GuardCallResult.Deny("decision_id required.");
            saved = await pending.GetAsync(a, id.Value.GetString()!, ct);
            if (saved is null) return GuardCallResult.Deny("Pending operation is unavailable or expired. Inspect evidence before starting another operation.");
            tool = saved.Tool;
            using var clear = JsonDocument.Parse(Protector(a).Unprotect(saved.ProtectedArguments));
            arguments = clear.RootElement.Clone();
        }
        var mapping = (await ListAsync(a, ct)).SingleOrDefault(m => m.McpServer + "__" + m.McpTool == tool);
        if (mapping is null) return GuardCallResult.Deny("Tool is not assigned to this agent and application.");
        var server = options.Value.Servers[mapping.McpServer];
        var binding = BindingHash(mapping, server);
        if (saved is not null && saved.BindingHash != binding) return GuardCallResult.Deny("Connection or mapping changed; new authorization required.");
        var hash = CanonicalJson.HashHex(System.Text.Json.Nodes.JsonNode.Parse(arguments.GetRawText()));
        DecisionDto decision;
        if (saved is not null) decision = await decisions.HandleAsync(a, saved.DecisionId, ct);
        else
        {
            if (arguments.GetRawText().Length > 65536) return GuardCallResult.Deny("Arguments exceed the supported limit.");
            var resource = ReadPath(arguments, mapping.ResourceIdPath);
            if (resource is not { ValueKind: JsonValueKind.String } || string.IsNullOrWhiteSpace(resource.Value.GetString())) return GuardCallResult.Deny("Mapped resource id missing.");
            var attributes = new Dictionary<string, JsonElement>();
            foreach (var (name, path) in GuardConfigHandler.ParseAttributePaths(mapping.AttributePathsJson))
                if (ReadPath(arguments, path) is { } value) attributes[name] = value.Clone();
            attributes["mcp_server"] = JsonSerializer.SerializeToElement(mapping.McpServer);
            attributes["mcp_tool"] = JsonSerializer.SerializeToElement(mapping.McpTool);
            attributes["destination"] = JsonSerializer.SerializeToElement(server.Url);
            if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 100 || operationId.Any(char.IsControl))
                return GuardCallResult.Deny("A stable _meta.deleguard_operation_id is required for protected calls.");
            var operation = new OperationRef(operationId, hash);
            decision = await authorize.HandleAsync(new(a, a.AgentExternalId, a.EnvironmentName, null, null, null,
                mapping.Action, mapping.ResourceType, resource.Value.GetString()!, attributes, hash, null, "mcp_" + operation.Id, "remote-mcp-guard", operation), ct);
            decision = await decisions.HandleAsync(a, decision.DecisionId, ct);
            if ((decision.Decision is "ALLOW" or "REQUIRE_APPROVAL") && await pending.GetAsync(a, decision.DecisionId, ct) is null)
                await pending.SaveAsync(a, new(decision.DecisionId, tool, Protector(a).Protect(arguments.GetRawText()), binding, DateTimeOffset.UtcNow.AddDays(7)), ct);
        }
        if (decision.Decision != "ALLOW") return GuardCallResult.Decision(decision);
        if (decision.Operation?.Hash != hash) return GuardCallResult.Deny("Arguments changed after authorization.");
        var claim = await execution.ConsumeAsync(a, decision.PermitId!, new(decision.Operation.Id, hash, decision.ResourceSnapshotHash!), ct);
        if (!claim.Acquired) return GuardCallResult.Deny("Operation already consumed. Inspect evidence; automatic replay is blocked.");
        try
        {
            await using var client = await ConnectAsync(server, ct);
            var result = await client.CallToolAsync(mapping.McpTool, JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.GetRawText()), cancellationToken: ct);
            var payload = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await evidence.HandleAsync(new(a, decision.DecisionId, decision.PermitId, result.IsError == true ? "FAILED" : "SUCCESS",
                server.Url, CanonicalJson.HashHex(payload.GetRawText()), decision.ResourceSnapshotHash, null, "remote-mcp-guard", claim.ExecutionId), ct);
            return GuardCallResult.Forwarded(decision, payload) with { IsError = result.IsError == true };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Remote MCP result uncertain for decision {Decision}", decision.DecisionId);
            try { await evidence.HandleAsync(new(a, decision.DecisionId, decision.PermitId, "UNKNOWN", server.Url, null,
                decision.ResourceSnapshotHash, null, "remote-mcp-guard", claim.ExecutionId), CancellationToken.None); } catch { }
            return GuardCallResult.Unknown(decision, claim.ExecutionId);
        }
    }
    private async Task<McpClient> ConnectAsync(RemoteGuardServerOptions s, CancellationToken ct)
    {
        if (!Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) ||
            WebhookUrlSafety.IsLocalhostHost(uri.Host) || !s.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new RequestValidationException("Remote MCP destination is not an allowed HTTPS host.");
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = uri,
            AdditionalHeaders = string.IsNullOrEmpty(s.BearerToken) ? null : new Dictionary<string, string> { ["Authorization"] = "Bearer " + s.BearerToken } },
            clients.CreateClient("remote-mcp-guard"));
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }
    private static JsonElement? ReadPath(JsonElement value, string path)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value)) return null;
        return value;
    }
}
public sealed record GuardCallResult(bool IsError, JsonElement Payload)
{
    public static GuardCallResult Unknown(DecisionDto d, string executionId) => new(true, JsonSerializer.SerializeToElement(new
        { decision = d.Decision, decision_id = d.DecisionId, execution_id = executionId, consumed = true, execution_state = "UNKNOWN", forwarded = (bool?)null,
          reason = "The provider may have executed the operation. Inspect evidence; automatic replay is blocked." }));
    public static GuardCallResult Deny(string reason) => new(true, JsonSerializer.SerializeToElement(new { decision = "DENY", forwarded = false, reason }));
    public static GuardCallResult Decision(DecisionDto d) => new(true, JsonSerializer.SerializeToElement(new { decision = d.Decision, decision_id = d.DecisionId, approval_id = d.ApprovalId, reason = d.Reason, forwarded = false }));
    public static GuardCallResult Forwarded(DecisionDto d, JsonElement result) => new(false, JsonSerializer.SerializeToElement(new { decision = d.Decision, decision_id = d.DecisionId, forwarded = true, downstream = result }));
}

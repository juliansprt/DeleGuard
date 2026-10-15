using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Deleguard.McpGuard;

public sealed class GuardEnforcer
{
    public const string GetDecisionTool = "get_decision";
    public const string OperationField = "_deleguard_operation_id";
    public const string Separator = "__";

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        GetDecisionTool,
        "approve",
        "deny",
        "approve_approval",
        "deny_approval"
    };

    private static readonly JsonElement GetDecisionSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("decision_id"),
        ["properties"] = new JsonObject
        {
            ["decision_id"] = new JsonObject { ["type"] = "string", ["description"] = "Decision id (dec_…) to poll after REQUIRE_APPROVAL." }
        }
    });

    private readonly GuardJournal _journal;
    private readonly IGuardConfigSource _config;
    private readonly IDeleguardPdp _pdp;
    private readonly DownstreamMcpHub _downstream;
    private readonly ConcurrentDictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GuardCallResult> _forwarded = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _executeLocks = new(StringComparer.Ordinal);

    public GuardEnforcer(IGuardConfigSource config, IDeleguardPdp pdp, DownstreamMcpHub downstream, GuardJournal? journal = null)
    {
        _config = config; _journal = journal ?? new GuardJournal();
        _pdp = pdp;
        _downstream = downstream;
    }

    public IReadOnlyList<string> ListedToolNames { get; private set; } = [GetDecisionTool];

    public async Task<IReadOnlyList<GuardListedTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var config = await _config.GetAsync(cancellationToken);
        var listed = new List<GuardListedTool>
        {
            new()
            {
                Name = GetDecisionTool,
                Description = "Poll a Deleguard decision after REQUIRE_APPROVAL. Humans approve in the console, never via this tool.",
                InputSchema = GetDecisionSchema
            }
        };

        foreach (var mapping in config.Mappings.OrderBy(RiskRank).ThenBy(item => item.Tool, StringComparer.Ordinal))
        {
            var schema = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = true,
                ["description"] = $"Protected by Deleguard action {mapping.Action}."
            });
            var downstream = _downstream.Get(mapping.McpServer);
            if (downstream is not null)
            {
                var tools = await downstream.ListToolsAsync(cancellationToken);
                var match = tools.FirstOrDefault(tool => string.Equals(tool.Name, mapping.McpTool, StringComparison.Ordinal));
                if (match is not null && match.InputSchema.ValueKind == JsonValueKind.Object)
                {
                    schema = match.InputSchema;
                }
            }

            var sideEffect = string.IsNullOrWhiteSpace(mapping.SideEffect) ? "idempotent" : mapping.SideEffect;
            var risk = string.IsNullOrWhiteSpace(mapping.Risk) ? "MEDIUM" : mapping.Risk;
            listed.Add(new GuardListedTool
            {
                Name = Compose(mapping.McpServer, mapping.McpTool),
                Description = $"Deleguard-guarded {mapping.McpServer}/{mapping.McpTool} → {mapping.Action} [risk={risk} side_effect={sideEffect}]. Unmapped tools are not listed and are denied.",
                InputSchema = WithOperationSchema(schema)
            });
        }

        ListedToolNames = listed.Select(tool => tool.Name).ToList();
        return listed;
    }

    public async Task<GuardCallResult> CallAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (Reserved.Contains(toolName) && !string.Equals(toolName, GetDecisionTool, StringComparison.Ordinal))
        {
            return Deny(null, "Guard cannot approve or deny. Humans use the Deleguard console.");
        }

        if (string.Equals(toolName, GetDecisionTool, StringComparison.Ordinal))
        {
            return await ResumeAsync(arguments, cancellationToken);
        }

        GuardConfigSnapshot config;
        try
        {
            config = await _config.GetFreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FailClosed(ex is GuardPdpUnavailableException ? ex.Message : "PDP unavailable.");
        }

        if (!TryParse(toolName, out var server, out var tool))
        {
            return Deny(null, "Unknown or unmapped tool.");
        }

        var mapping = config.Mappings.FirstOrDefault(item =>
            string.Equals(item.McpServer, server, StringComparison.Ordinal) &&
            string.Equals(item.McpTool, tool, StringComparison.Ordinal));
        if (mapping is null)
        {
            return Deny(null, "Unknown or unmapped tool.");
        }

        if (!ResourceHydrator.TryReadString(arguments, mapping.ResourceIdPath, out var resourceId))
        {
            return Deny(null, $"Missing resource id at '{mapping.ResourceIdPath}'.");
        }

        if (!ResourceHydrator.TryReadString(arguments, OperationField, out var operationId) || operationId.Length > 100 || operationId.Any(char.IsControl))
            return Deny(null, "A stable _deleguard_operation_id is required. Keep it for retries; use a new identity for a new operation.");
        var clean = JsonNode.Parse(arguments.GetRawText())!.AsObject();
        clean.Remove(OperationField);
        arguments = JsonSerializer.SerializeToElement(clean);
        var connectionHash = _downstream.Get(mapping.McpServer)?.ConnectionHash;
        if (string.IsNullOrWhiteSpace(connectionHash)) return Deny(null, "Downstream connection binding is unavailable.");
        var attributes = ResourceHydrator.ReadAttributes(arguments, mapping.AttributePaths);
        var context = new Dictionary<string, string>
        {
            ["source"] = "mcp-guard",
            ["mcp_server"] = mapping.McpServer,
            ["mcp_tool"] = mapping.McpTool,
            ["operation_hash"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DeleguardPdpClient.CanonicalJson(JsonSerializer.SerializeToElement(new { arguments, mapping, connectionHash }))))).ToLowerInvariant()
        };
        var idempotency = "grd_" + operationId;

        PdpDecision authorized;
        try
        {
            authorized = await _pdp.AuthorizeAsync(
                mapping.Action,
                mapping.ResourceType,
                resourceId,
                attributes,
                context,
                idempotency,
                config.Environment,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(authorized.DecisionId) &&
                (string.Equals(authorized.Decision, "REQUIRE_APPROVAL", StringComparison.Ordinal) ||
                 string.Equals(authorized.Decision, "ALLOW", StringComparison.Ordinal)))
            {
                authorized = await _pdp.GetDecisionAsync(authorized.DecisionId, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FailClosed(ex is GuardPdpUnavailableException ? ex.Message : "PDP unavailable.");
        }

        return await ApplyDecisionAsync(mapping, arguments, authorized, cancellationToken);
    }

    private async Task<GuardCallResult> ResumeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!ResourceHydrator.TryReadString(arguments, "decision_id", out var decisionId))
        {
            return Deny(null, "decision_id is required.");
        }

        PdpDecision decision;
        try
        {
            decision = await _pdp.GetDecisionAsync(decisionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FailClosed(ex is GuardPdpUnavailableException ? ex.Message : "PDP unavailable.");
        }

        if (!_pending.TryGetValue(decisionId, out var pending) && _journal.Pending.Load(decisionId) is { } stored)
            pending = new PendingCall(stored.Mapping, stored.Arguments, stored.ConnectionHash);
        if (pending is null)
        {
            return Envelope(
                true,
                decision,
                forwarded: false,
                "The Guard does not have the original tool arguments (process restart or unknown decision_id). Inspect the original operation before retrying the original guarded tool with exactly the same operation identity and arguments.");
        }

        if (string.IsNullOrWhiteSpace(pending.ConnectionHash) ||
            pending.ConnectionHash != _downstream.Get(pending.Mapping.McpServer)?.ConnectionHash)
            return Deny(decision, "Downstream destination or credentials changed; request a new authorization.");

        GuardMapping mapping;
        try
        {
            var config = await _config.GetFreshAsync(cancellationToken);
            var current = config.Mappings.FirstOrDefault(item =>
                string.Equals(item.McpServer, pending.Mapping.McpServer, StringComparison.Ordinal) &&
                string.Equals(item.McpTool, pending.Mapping.McpTool, StringComparison.Ordinal) &&
                string.Equals(item.Action, pending.Mapping.Action, StringComparison.Ordinal));
            if (current is null || DeleguardPdpClient.CanonicalJson(JsonSerializer.SerializeToElement(current)) !=
                DeleguardPdpClient.CanonicalJson(JsonSerializer.SerializeToElement(pending.Mapping)))
            {
                return Deny(decision, "MCP binding was removed; downstream will not be called.");
            }

            mapping = current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FailClosed(ex is GuardPdpUnavailableException ? ex.Message : "PDP unavailable.");
        }

        return await ApplyDecisionAsync(mapping, pending.Arguments, decision, cancellationToken);
    }

    private async Task<GuardCallResult> ApplyDecisionAsync(
        GuardMapping mapping,
        JsonElement arguments,
        PdpDecision decision,
        CancellationToken cancellationToken)
    {
        if (string.Equals(decision.Decision, "DENY", StringComparison.Ordinal))
        {
            return Deny(decision, decision.Reason ?? "Denied by policy.");
        }

        if (string.Equals(decision.Decision, "REQUIRE_APPROVAL", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(decision.DecisionId))
            {
                var binding = _downstream.Get(mapping.McpServer)?.ConnectionHash;
                if (string.IsNullOrWhiteSpace(binding)) return Deny(decision, "Downstream connection binding is unavailable.");
                _journal.Pending.Save(decision.DecisionId, mapping, arguments, binding);
                _pending[decision.DecisionId] = new PendingCall(mapping, arguments.Clone(), binding);
            }

            return Envelope(true, decision, false, decision.Reason ?? "Waiting for human approval in the Deleguard console. Poll with get_decision.");
        }

        if (!string.Equals(decision.Decision, "ALLOW", StringComparison.Ordinal))
        {
            return FailClosed($"Unexpected decision '{decision.Decision}'.");
        }

        if (string.IsNullOrWhiteSpace(decision.Permit) ||
            decision.ExpiresAt is not { } expiresAt ||
            expiresAt <= DateTimeOffset.UtcNow)
        {
            return Envelope(true, decision, false, "Permit missing or expired; reauthorization is required.");
        }

        var gate = _executeLocks.GetOrAdd(decision.DecisionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_forwarded.TryGetValue(decision.DecisionId, out var previous))
            {
                return previous;
            }

            var downstream = _downstream.Get(mapping.McpServer);
            if (downstream is null)
            {
                await TryReportAsync(decision.DecisionId, "FAILED", "downstream_missing", cancellationToken);
                return Envelope(true, decision, false, $"Downstream MCP '{mapping.McpServer}' is not configured.");
            }

            if (!_journal.TryClaim(decision.DecisionId))
                return Envelope(true, decision, false, "This decision was already released before this process started. Inspect evidence; uncertain operations are never replayed automatically.");

            try
            {
                if (!await _pdp.ConsumeAsync(decision, cancellationToken))
                    return Envelope(true, decision, false, "Permit already consumed; inspect evidence. Automatic replay is blocked.");
            }
            catch (Exception) { return FailClosed("Permit consumption unavailable; downstream was not called."); }
            DownstreamCallResult forwarded;
            try
            {
                forwarded = await downstream.CallAsync(mapping.McpTool, arguments, cancellationToken);
            }
            catch (Exception)
            {
                await TryReportAsync(decision.DecisionId, "UNKNOWN", "downstream_error", cancellationToken);
                var uncertain = JsonSerializer.SerializeToElement(new { decision = decision.Decision, decision_id = decision.DecisionId,
                    consumed = true, execution_state = "UNKNOWN", forwarded = (bool?)null, reason = "The tool may have executed. Automatic replay is blocked." });
                return new GuardCallResult { IsError = true, Text = uncertain.GetRawText(), Structured = uncertain };
            }

            var result = new GuardCallResult
            {
                IsError = forwarded.IsError,
                Text = forwarded.Text,
                Structured = JsonSerializer.SerializeToElement(new
                {
                    result_type = "complete",
                    decision = decision.Decision,
                    decision_id = decision.DecisionId,
                    forwarded = true,
                    downstream = forwarded.Structured
                }),
                Forwarded = true
            };
            _forwarded[decision.DecisionId] = result;
            _pending.TryRemove(decision.DecisionId, out _);
            _journal.Pending.Remove(decision.DecisionId);
            await TryReportAsync(decision.DecisionId, forwarded.IsError ? "FAILED" : "SUCCESS", mapping.McpTool, cancellationToken);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task TryReportAsync(string decisionId, string status, string? reference, CancellationToken cancellationToken)
    {
        try
        {
            _journal.Queue(decisionId, status, reference);
            await _journal.FlushAsync(_pdp, cancellationToken);
        }
        catch (Exception)
        {
            // Evidence is off the ALLOW path. The tool already ran or failed; do not invent ALLOW.
        }
    }

    private static GuardCallResult Deny(PdpDecision? decision, string reason) =>
        Envelope(true, decision, false, reason);

    private static GuardCallResult FailClosed(string reason) =>
        Envelope(true, null, false, reason, failClosed: true);

    private static GuardCallResult Envelope(
        bool isError,
        PdpDecision? decision,
        bool forwarded,
        string message,
        bool failClosed = false)
    {
        var structured = JsonSerializer.SerializeToElement(new
        {
            result_type = "complete",
            decision = failClosed ? "FAIL_CLOSED" : decision?.Decision ?? "DENY",
            decision_id = decision?.DecisionId,
            approval_id = decision?.ApprovalId,
            reason = message,
            forwarded
        });
        return new GuardCallResult
        {
            IsError = isError,
            Text = structured.GetRawText(),
            Structured = structured,
            Forwarded = forwarded
        };
    }

    private static JsonElement WithOperationSchema(JsonElement schema)
    {
        var result = JsonNode.Parse(schema.GetRawText())!.AsObject();
        var properties = result["properties"] as JsonObject ?? new JsonObject();
        if (result["properties"] is null) result["properties"] = properties;
        properties[OperationField] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 100,
            ["description"] = "Unique operation identity. Reuse exactly on retries; a new legitimate action needs a new value." };
        var required = result["required"] as JsonArray ?? new JsonArray();
        if (result["required"] is null) result["required"] = required;
        required.Add(OperationField);
        return JsonSerializer.SerializeToElement(result);
    }

    private static int RiskRank(GuardMapping mapping)
    {
        return mapping.Risk.ToUpperInvariant() switch
        {
            "LOW" => 0,
            "MEDIUM" => 1,
            "HIGH" => 2,
            "CRITICAL" => 3,
            _ => 1
        };
    }

    public static string Compose(string server, string tool) => server + Separator + tool;

    public static bool TryParse(string toolName, out string server, out string tool)
    {
        server = string.Empty;
        tool = string.Empty;
        var index = toolName.IndexOf(Separator, StringComparison.Ordinal);
        if (index <= 0 || index + Separator.Length >= toolName.Length)
        {
            return false;
        }

        server = toolName[..index];
        tool = toolName[(index + Separator.Length)..];
        return true;
    }

    private sealed record PendingCall(GuardMapping Mapping, JsonElement Arguments, string? ConnectionHash);
}

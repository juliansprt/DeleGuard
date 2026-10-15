using System.Globalization;
using System.Text.Json;
using Deleguard.Application.Audit;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;

namespace Deleguard.Api.Pages;

public sealed record CanonicalFactRow(string Label, string Value);

public sealed class CanonicalFactsView
{
    public string? Agent { get; init; }
    public string? Actor { get; init; }
    public string? Action { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? Environment { get; init; }
    public string? PolicyId { get; init; }
    public string? PolicyVersion { get; init; }
    public string? Amount { get; init; }
    public string? Status { get; init; }
    public IReadOnlyList<CanonicalFactRow> Rows { get; init; } = [];

    public string Headline
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Action) && !string.IsNullOrWhiteSpace(ResourceId))
            {
                return $"{Action} · {ResourceId}";
            }

            return Action ?? ResourceId ?? new Deleguard.Api.Localization.UiText()["Pending approval"];
        }
    }

    public static CanonicalFactsView Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CanonicalFactsView();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var agent = ReadString(root, "agent");
            var actor = ReadString(root, "actor");
            var action = ReadString(root, "action");
            var resourceType = ReadString(root, "resource_type");
            var resourceId = ReadString(root, "resource_id");
            var environment = ReadString(root, "environment");
            var policyId = ReadString(root, "policy_id");
            var policyVersion = ReadAny(root, "policy_version");
            string? amount = null;
            string? status = null;
            if (root.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
            {
                amount = FormatAmount(attributes);
                status = ReadString(attributes, "status");
            }

            var rows = new List<CanonicalFactRow>();
            Add(rows, "Agent", agent);
            Add(rows, "Action", action);
            Add(rows, "Amount", amount);
            Add(rows, "Resource", resourceId);
            Add(rows, "Environment", environment);
            Add(rows, "Status", status);
            Add(rows, "Type", resourceType);
            Add(rows, "Actor", actor);
            Add(rows, "Policy", string.IsNullOrWhiteSpace(policyId) ? null : $"{policyId}{(string.IsNullOrWhiteSpace(policyVersion) ? "" : $" v{policyVersion}")}");
            return new CanonicalFactsView
            {
                Agent = agent,
                Actor = actor,
                Action = action,
                ResourceType = resourceType,
                ResourceId = resourceId,
                Environment = environment,
                PolicyId = policyId,
                PolicyVersion = policyVersion,
                Amount = amount,
                Status = status,
                Rows = rows
            };
        }
        catch (JsonException)
        {
            return new CanonicalFactsView();
        }
    }

    private static void Add(List<CanonicalFactRow> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new CanonicalFactRow(label, value));
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadAny(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? FormatAmount(JsonElement attributes)
    {
        if (!attributes.TryGetProperty("amount", out var amount))
        {
            return null;
        }

        if (amount.ValueKind == JsonValueKind.Number && amount.TryGetInt64(out var number))
        {
            return number.ToString("N0", CultureInfo.InvariantCulture);
        }

        return amount.ToString();
    }
}

public static class ConsoleFormatting
{
    public static string DecisionBadge(string? decision) => decision switch
    {
        "ALLOW" or "APPROVE" => "allow",
        "DENY" => "deny",
        "REQUIRE_APPROVAL" => "wait",
        _ => "neutral"
    };

    public static string RiskBadge(RiskLevel risk) => risk switch
    {
        RiskLevel.Low => "allow",
        RiskLevel.Medium => "wait",
        RiskLevel.High => "high",
        RiskLevel.Critical => "deny",
        _ => "neutral"
    };

    public static string RiskLabel(RiskLevel risk) => risk.ToString().ToUpperInvariant();

    public static string EventLabel(string eventType) => eventType switch
    {
        "authorization.decided" => "Decision",
        "approval.resolved" => "Approval",
        "approval.expired" => "Expired",
        "execution.reported" => "Execution",
        "webhook.endpoint.disabled" => "Webhook disabled",
        "webhook.ping" => "Webhook ping",
        _ => eventType
    };

    public static string? EventOutcome(AuditEvent item)
    {
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            var root = document.RootElement;
            return item.EventType switch
            {
                "authorization.decided" => Read(root, "decision"),
                "approval.resolved" => Read(root, "action"),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string EventDetail(AuditEvent item)
    {
        var summary = EventSummary(item);
        var outcome = EventOutcome(item);
        if (string.IsNullOrWhiteSpace(outcome) || !summary.StartsWith(outcome, StringComparison.Ordinal))
        {
            return summary;
        }

        var rest = summary[outcome.Length..].TrimStart(' ', '·');
        return string.IsNullOrWhiteSpace(rest) ? summary : rest;
    }

    public static string ShortId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "—";
        }

        if (id.Length <= 16)
        {
            return id;
        }

        return $"{id[..8]}…{id[^6..]}";
    }

    public static bool IsPlaygroundEvent(AuditEvent item) =>
        AuditPayload.IsPlaygroundEvent(item.EventType, item.PayloadJson);

    public static string EventSummary(AuditEvent item)
    {
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            var root = document.RootElement;
            if (item.EventType == "authorization.decided")
            {
                var decision = Read(root, "decision");
                var action = Read(root, "action");
                var resource = Read(root, "resource_id");
                return string.Join(" · ", new[] { decision, action, resource }.Where(v => !string.IsNullOrWhiteSpace(v)));
            }

            if (item.EventType == "approval.resolved")
            {
                return $"{Read(root, "action")} · {Read(root, "actor")}".Trim(' ', '·');
            }

            if (item.EventType == "execution.reported")
            {
                return Read(root, "resource_id") ?? string.Empty;
            }

            if (item.EventType == "approval.expired")
            {
                return $"{Read(root, "approval_id")} expired";
            }

            if (item.EventType == "webhook.endpoint.disabled")
            {
                return $"{Read(root, "endpoint_id")} · {Read(root, "reason")}";
            }

            return item.EventType;
        }
        catch (JsonException)
        {
            return item.EventType;
        }
    }

    public static string RelativeExpiry(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var delta = expiresAt - now;
        if (delta.TotalSeconds <= 0)
        {
            return new Deleguard.Api.Localization.UiText()["Expired"];
        }

        if (delta.TotalHours >= 1)
        {
            return new Deleguard.Api.Localization.UiText().Format("expiry.hours", Math.Ceiling(delta.TotalHours));
        }

        return new Deleguard.Api.Localization.UiText().Format("expiry.minutes", Math.Max(1, (int)Math.Ceiling(delta.TotalMinutes)));
    }

    public static string PrettyJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;
}

public sealed record EmptyStateModel(string Title, string Body, string? ActionHref = null, string? ActionLabel = null);

public sealed record FlashModel(string? Message, bool IsError, bool Secret = false);

public static class WebhookConsole
{
    public static string StatusBadge(string status) => status switch
    {
        "enabled" => "allow",
        "failing" => "wait",
        "disabled" => "deny",
        _ => "neutral"
    };
}

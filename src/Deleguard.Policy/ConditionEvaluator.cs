using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Deleguard.Policy;

public static class ConditionEvaluator
{
    public static bool Matches(ConditionNode node, AuthorizationEnvelope envelope) => node switch
    {
        ConditionNode.Always => true,
        ConditionNode.All all => all.Items.All(item => Matches(item, envelope)),
        ConditionNode.Any any => any.Items.Count == 0 || any.Items.Any(item => Matches(item, envelope)),
        ConditionNode.Field field => MatchesField(field, envelope),
        _ => false
    };

    private static bool MatchesField(ConditionNode.Field field, AuthorizationEnvelope envelope)
    {
        var actual = Resolve(envelope, field.Path);
        var op = field.Op.ToLowerInvariant();
        if (op is "exists")
        {
            return actual is not null;
        }

        if (actual is null)
        {
            return false;
        }

        if (op is "contains")
        {
            var needle = Convert.ToString(field.Value, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(needle))
            {
                return false;
            }

            if (actual is IEnumerable<string> items)
            {
                return items.Contains(needle, StringComparer.Ordinal);
            }

            var haystack = Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty;
            return haystack.Contains(needle, StringComparison.Ordinal);
        }

        if (op is "in")
        {
            var actualText = Convert.ToString(actual, CultureInfo.InvariantCulture);
            if (field.Value is IEnumerable<string> allowed)
            {
                return allowed.Contains(actualText, StringComparer.Ordinal);
            }

            if (field.Value is IEnumerable enumerable)
            {
                return enumerable.Cast<object?>().Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))
                    .Contains(actualText, StringComparer.Ordinal);
            }

            return false;
        }

        return op switch
        {
            "eq" => Compare(actual, field.Value) == 0,
            "ne" => Compare(actual, field.Value) != 0,
            "gt" => Compare(actual, field.Value) > 0,
            "gte" => Compare(actual, field.Value) >= 0,
            "lt" => Compare(actual, field.Value) < 0,
            "lte" => Compare(actual, field.Value) <= 0,
            _ => false
        };
    }

    private static object? Resolve(AuthorizationEnvelope envelope, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        return parts[0] switch
        {
            "action" => envelope.Action,
            "agent" when parts.Length > 1 && parts[1] == "id" => envelope.AgentId,
            "agent" when parts.Length > 1 && parts[1] == "environment" => envelope.Environment,
            "actor" when parts.Length > 1 && parts[1] == "id" => envelope.ActorId,
            "actor" when parts.Length > 1 && parts[1] == "type" => envelope.ActorType,
            "actor" when parts.Length > 1 && parts[1] == "roles" =>
                envelope.ActorRoles is null ? null : envelope.ActorRoles,
            "resource" when parts.Length > 1 && parts[1] == "type" => envelope.ResourceType,
            "resource" when parts.Length > 1 && parts[1] == "id" => envelope.ResourceId,
            "resource" when parts.Length > 2 && parts[1] == "attributes" =>
                GetAttribute(envelope.ResourceAttributes, string.Join('.', parts.Skip(2))),
            "context" when parts.Length > 1 =>
                GetAttribute(envelope.Context, string.Join('.', parts.Skip(1))),
            _ => null
        };
    }

    private static object? GetAttribute(IReadOnlyDictionary<string, JsonElement> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static int Compare(object actual, object? expected)
    {
        if (actual is bool ab && expected is bool eb)
        {
            return ab.CompareTo(eb);
        }

        if (TryNumber(actual, out var an) && TryNumber(expected, out var en))
        {
            return an.CompareTo(en);
        }

        return string.Compare(Convert.ToString(actual, CultureInfo.InvariantCulture), Convert.ToString(expected, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static bool TryNumber(object? value, out decimal number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case float or double or decimal:
                number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed):
                number = parsed;
                return true;
            default:
                number = 0;
                return false;
        }
    }
}

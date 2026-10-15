using Deleguard.Domain.Audit;

namespace Deleguard.Application.Audit;

public static class AuditListFilter
{
    public static bool Matches(AuditEvent item, string? eventType, string? q)
    {
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var prefix = eventType.Trim();
            if (!(string.Equals(item.EventType, prefix, StringComparison.Ordinal)
                  || item.EventType.StartsWith(prefix + ".", StringComparison.Ordinal)))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        var term = q.Trim();
        return (item.DecisionId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || item.EventType.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.PayloadJson.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}

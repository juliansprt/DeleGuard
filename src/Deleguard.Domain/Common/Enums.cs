namespace Deleguard.Domain.Common;

public enum DecisionEffect
{
    Deny = 0,
    RequireApproval = 1,
    Allow = 2
}

public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Expired = 3
}

public enum ExecutionStatus
{
    Success = 0,
    Failed = 1,
    Partial = 2,
    Unknown = 3
}

public enum ActorType
{
    Human = 0,
    Service = 1
}

public static class DecisionNames
{
    public const string Allow = "ALLOW";
    public const string Deny = "DENY";
    public const string RequireApproval = "REQUIRE_APPROVAL";

    public static string ToApi(DecisionEffect effect) => effect switch
    {
        DecisionEffect.Allow => Allow,
        DecisionEffect.RequireApproval => RequireApproval,
        _ => Deny
    };

    public static DecisionEffect Parse(string value) => value.ToUpperInvariant() switch
    {
        Allow => DecisionEffect.Allow,
        RequireApproval => DecisionEffect.RequireApproval,
        _ => DecisionEffect.Deny
    };

    public static int Precedence(DecisionEffect effect) => effect switch
    {
        DecisionEffect.Deny => 3,
        DecisionEffect.RequireApproval => 2,
        DecisionEffect.Allow => 1,
        _ => 0
    };
}

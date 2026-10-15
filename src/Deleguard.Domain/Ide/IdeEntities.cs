namespace Deleguard.Domain.Ide;

public sealed class IdeProject
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool CaseSensitive { get; set; }
    public bool ShareDiff { get; set; }
    public string ProtectedPathsJson { get; set; } = "[]";
    public string DeniedPathsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class IdeSession
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Adapter { get; set; } = "";
    public string Version { get; set; } = "";
    public string Platform { get; set; } = "";
    public string CapabilitiesJson { get; set; } = "{}";
    public DateTimeOffset LastSeenAt { get; set; }
}
public sealed class IdeGrant
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Directory { get; set; } = "";
    public string ActionsJson { get; set; } = "[]";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class IdePermitUse
{
    public string DecisionId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
    public DateTimeOffset ConsumedAt { get; set; }
}

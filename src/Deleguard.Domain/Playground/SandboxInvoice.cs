namespace Deleguard.Domain.Playground;

public sealed class SandboxInvoice
{
    public const string StatusDraft = "draft";
    public const string StatusIssued = "issued";
    public const string StatusCancelled = "cancelled";

    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string DisplayId { get; set; }
    public required string Status { get; set; }
    public long Amount { get; set; }
    public bool ReportedToTaxAuthority { get; set; }
    public string? LastDecisionId { get; set; }
    public string? LastApprovalId { get; set; }
    public string? LastDecision { get; set; }
    public string? LastReason { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; set; }

    public bool IsCancelled => Status == StatusCancelled;
}

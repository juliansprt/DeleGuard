namespace Deleguard.DemoAgent.Agent;

public sealed class DemoInvoice
{
    public const string StatusDraft = "draft";
    public const string StatusIssued = "issued";
    public const string StatusCancelled = "cancelled";

    public required string Id { get; init; }
    public required string Status { get; set; }
    public required long Amount { get; set; }
    public required bool ReportedToTaxAuthority { get; set; }
    public string Currency { get; init; } = "COP";
    public string? LastDecisionId { get; set; }
    public string? LastApprovalId { get; set; }
    public string? LastDecision { get; set; }
    public string? LastReason { get; set; }
    public string? LastPermit { get; set; }

    public bool IsCancelled => string.Equals(Status, StatusCancelled, StringComparison.OrdinalIgnoreCase);
}

public sealed record DemoInvoiceFacts(
    string Id,
    string Status,
    long Amount,
    bool ReportedToTaxAuthority,
    bool Cancelled);

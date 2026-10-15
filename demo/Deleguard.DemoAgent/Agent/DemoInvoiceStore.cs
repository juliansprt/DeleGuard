using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Deleguard.DemoAgent.Agent;

public sealed class DemoInvoiceStore : IDemoInvoiceStore
{
    private static readonly Regex InvoiceIdPattern = new(@"^FAC-[A-Z0-9][A-Z0-9_-]{0,26}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, DemoInvoice> _invoices = new(StringComparer.OrdinalIgnoreCase);

    public DemoInvoiceStore() => Seed();

    public IReadOnlyList<DemoInvoice> List() =>
        _invoices.Values.OrderBy(invoice => invoice.Id, StringComparer.OrdinalIgnoreCase).ToList();

    public DemoInvoice? Get(string id) =>
        _invoices.TryGetValue(id, out var invoice) ? invoice : null;

    public void RememberDecision(DemoInvoice invoice, DeleguardDecision decision)
    {
        invoice.LastDecisionId = decision.DecisionId;
        invoice.LastApprovalId = decision.ApprovalId;
        invoice.LastDecision = decision.Decision;
        invoice.LastReason = decision.Reason;
        invoice.LastPermit = decision.Permit;
    }

    public void Cancel(DemoInvoice invoice)
    {
        invoice.Status = DemoInvoice.StatusCancelled;
    }

    public void Reset()
    {
        _invoices.Clear();
        Seed();
    }

    public void ApplyEdits(IReadOnlyList<InvoiceDraft> drafts)
    {
        if (drafts.Count == 0)
        {
            throw new ArgumentException("Add at least one invoice.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in drafts)
        {
            Validate(draft);
            if (!seen.Add(draft.Id.Trim()))
            {
                throw new ArgumentException($"Duplicate invoice id '{draft.Id.Trim()}'.");
            }
        }

        var next = new Dictionary<string, DemoInvoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in drafts)
        {
            var id = NormalizeId(draft.Id);
            var status = NormalizeStatus(draft.Status);
            var existing = Get(draft.OriginalId) ?? Get(id);
            var factsChanged = existing is null
                || existing.Amount != draft.Amount
                || existing.ReportedToTaxAuthority != draft.ReportedToTaxAuthority
                || !string.Equals(existing.Status, status, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase);

            next[id] = new DemoInvoice
            {
                Id = id,
                Status = status,
                Amount = draft.Amount,
                ReportedToTaxAuthority = draft.ReportedToTaxAuthority,
                LastDecisionId = factsChanged ? null : existing?.LastDecisionId,
                LastApprovalId = factsChanged ? null : existing?.LastApprovalId,
                LastDecision = factsChanged ? null : existing?.LastDecision,
                LastReason = factsChanged ? null : existing?.LastReason,
                LastPermit = factsChanged ? null : existing?.LastPermit
            };
        }

        _invoices.Clear();
        foreach (var invoice in next.Values)
        {
            Upsert(invoice);
        }
    }

    private static void Validate(InvoiceDraft draft)
    {
        var id = draft.Id.Trim();
        if (!InvoiceIdPattern.IsMatch(id))
        {
            throw new ArgumentException("Invoice id must look like FAC-DRAFT (FAC- plus letters, digits, _ or -).");
        }

        if (draft.Amount < 0)
        {
            throw new ArgumentException($"Amount for {id} must be zero or greater.");
        }

        NormalizeStatus(draft.Status);
    }

    private static string NormalizeId(string id) => id.Trim().ToUpperInvariant();

    private static string NormalizeStatus(string status)
    {
        var value = status.Trim().ToLowerInvariant();
        return value switch
        {
            DemoInvoice.StatusDraft or DemoInvoice.StatusIssued or DemoInvoice.StatusCancelled => value,
            _ => throw new ArgumentException("Status must be draft, issued, or cancelled.")
        };
    }

    private void Seed()
    {
        Upsert(new DemoInvoice
        {
            Id = "FAC-DRAFT",
            Status = DemoInvoice.StatusDraft,
            Amount = 150_000,
            ReportedToTaxAuthority = false
        });
        Upsert(new DemoInvoice
        {
            Id = "FAC-HIGH",
            Status = DemoInvoice.StatusIssued,
            Amount = 25_000_000,
            ReportedToTaxAuthority = false
        });
        Upsert(new DemoInvoice
        {
            Id = "FAC-TAX",
            Status = DemoInvoice.StatusIssued,
            Amount = 800_000,
            ReportedToTaxAuthority = true
        });
    }

    private void Upsert(DemoInvoice invoice) => _invoices[invoice.Id] = invoice;
}

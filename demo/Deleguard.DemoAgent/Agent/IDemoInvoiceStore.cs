namespace Deleguard.DemoAgent.Agent;

public sealed record InvoiceDraft(string OriginalId, string Id, string Status, long Amount, bool ReportedToTaxAuthority);

public interface IDemoInvoiceStore
{
    IReadOnlyList<DemoInvoice> List();
    DemoInvoice? Get(string id);
    void RememberDecision(DemoInvoice invoice, DeleguardDecision decision);
    void Cancel(DemoInvoice invoice);
    void Reset();
    void ApplyEdits(IReadOnlyList<InvoiceDraft> drafts);
}

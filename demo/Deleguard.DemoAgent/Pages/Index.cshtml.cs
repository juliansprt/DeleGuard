using System.Globalization;
using Deleguard.DemoAgent.Agent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.DemoAgent.Pages;

public sealed class IndexModel : PageModel
{
    private readonly DemoAgentHandler _handler;
    private readonly LlmOptions _llm;

    public IndexModel(DemoAgentHandler handler, IOptions<LlmOptions> llm)
    {
        _handler = handler;
        _llm = llm.Value;
    }

    public IReadOnlyList<DemoInvoice> Invoices { get; private set; } = [];
    public IReadOnlyList<ChatTurn> Turns { get; private set; } = [];
    public bool LlmConfigured { get; private set; }
    public string LlmModel { get; private set; } = string.Empty;
    public string? InvoiceMessage { get; private set; }
    public bool InvoiceError { get; private set; }

    [BindProperty]
    public string? Message { get; set; }

    [BindProperty]
    public List<InvoiceEditRow> Rows { get; set; } = [];

    [BindProperty]
    public InvoiceEditRow NewInvoice { get; set; } = new();

    public void OnGet() => BindState();

    public async Task<IActionResult> OnPostChatAsync(CancellationToken cancellationToken)
    {
        await _handler.HandleUserMessageAsync(Message ?? string.Empty, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(string invoiceId, CancellationToken cancellationToken)
    {
        await _handler.RetryAsync(invoiceId, cancellationToken);
        return RedirectToPage();
    }

    public IActionResult OnPostReset()
    {
        _handler.Reset();
        return RedirectToPage();
    }

    public IActionResult OnPostSaveInvoices()
    {
        try
        {
            var drafts = new List<InvoiceDraft>();
            foreach (var row in Rows)
            {
                if (row.Remove || string.IsNullOrWhiteSpace(row.Id))
                {
                    continue;
                }

                drafts.Add(ToDraft(row));
            }

            if (!string.IsNullOrWhiteSpace(NewInvoice.Id))
            {
                NewInvoice.OriginalId = NewInvoice.Id;
                drafts.Add(ToDraft(NewInvoice));
            }

            _handler.SaveInvoices(drafts);
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            InvoiceError = true;
            InvoiceMessage = ex.Message;
            BindState(keepEditor: true);
            return Page();
        }
    }

    private void BindState(bool keepEditor = false)
    {
        Invoices = _handler.Invoices;
        Turns = _handler.Turns;
        LlmConfigured = !string.IsNullOrWhiteSpace(_llm.ApiKey);
        LlmModel = _llm.Model;
        if (!keepEditor)
        {
            Rows = Invoices.Select(invoice => new InvoiceEditRow
            {
                OriginalId = invoice.Id,
                Id = invoice.Id,
                Status = invoice.Status,
                AmountText = invoice.Amount.ToString("N0", CultureInfo.InvariantCulture),
                ReportedToTaxAuthority = invoice.ReportedToTaxAuthority
            }).ToList();
            NewInvoice = new InvoiceEditRow { Status = DemoInvoice.StatusIssued };
        }
    }

    private static InvoiceDraft ToDraft(InvoiceEditRow row)
    {
        if (!TryParseAmount(row.AmountText, out var amount))
        {
            throw new FormatException($"Amount for {row.Id} must be a whole number.");
        }

        return new InvoiceDraft(row.OriginalId, row.Id, row.Status, amount, row.ReportedToTaxAuthority);
    }

    private static bool TryParseAmount(string? text, out long amount)
    {
        var compact = (text ?? string.Empty).Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();
        return long.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount);
    }

    public sealed class InvoiceEditRow
    {
        public string OriginalId { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = DemoInvoice.StatusDraft;
        public string AmountText { get; set; } = "0";
        public bool ReportedToTaxAuthority { get; set; }
        public bool Remove { get; set; }
    }
}

using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Deleguard.DemoAgent.Agent;

public sealed class ChatTurn
{
    public required string UserText { get; init; }
    public required string AssistantText { get; init; }
    public InducedIntent? Intent { get; init; }
    public DemoInvoiceFacts? Facts { get; init; }
    public DeleguardDecision? Decision { get; init; }
    public string? ApprovalUrl { get; init; }
    public bool CanRetry { get; init; }
    public string? InvoiceId { get; init; }
    public bool Executed { get; init; }
    public string? HallucinationNote { get; init; }
}

public sealed class ChatTranscript
{
    private readonly List<ChatTurn> _turns = [];
    private readonly object _gate = new();

    public IReadOnlyList<ChatTurn> List()
    {
        lock (_gate)
        {
            return _turns.ToList();
        }
    }

    public void Add(ChatTurn turn)
    {
        lock (_gate)
        {
            _turns.Add(turn);
            const int keep = 40;
            if (_turns.Count > keep)
            {
                _turns.RemoveRange(0, _turns.Count - keep);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _turns.Clear();
        }
    }
}

public sealed class DemoAgentHandler
{
    public const string CancelAction = "invoice.cancel";

    private readonly IIntentInducer _inducer;
    private readonly IDemoInvoiceStore _store;
    private readonly IDeleguardClient _deleguard;
    private readonly ChatTranscript _transcript;
    private readonly DeleguardOptions _options;

    public DemoAgentHandler(
        IIntentInducer inducer,
        IDemoInvoiceStore store,
        IDeleguardClient deleguard,
        ChatTranscript transcript,
        IOptions<DeleguardOptions> options)
    {
        _inducer = inducer;
        _store = store;
        _deleguard = deleguard;
        _transcript = transcript;
        _options = options.Value;
    }

    public IReadOnlyList<DemoInvoice> Invoices => _store.List();
    public IReadOnlyList<ChatTurn> Turns => _transcript.List();

    public void Reset()
    {
        _store.Reset();
        _transcript.Clear();
    }

    public void SaveInvoices(IReadOnlyList<InvoiceDraft> drafts) => _store.ApplyEdits(drafts);

    public async Task<ChatTurn> HandleUserMessageAsync(string userText, CancellationToken cancellationToken)
    {
        var text = (userText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return Record(new ChatTurn
            {
                UserText = text,
                AssistantText = "Write a message, for example: cancel invoice FAC-DRAFT."
            });
        }

        var intent = StubIntentInducer.MergeUserClaims(text, await _inducer.InduceAsync(text, cancellationToken));
        if (!string.Equals(intent.Action, CancelAction, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(intent.ResourceId))
        {
            var known = string.Join(", ", _store.List().Select(invoice => invoice.Id));
            var scope = string.IsNullOrWhiteSpace(known) ? "the demo table" : known;
            return Record(new ChatTurn
            {
                UserText = text,
                Intent = intent,
                AssistantText = $"I did not identify a cancel for an invoice in the table ({scope}). Deleguard was not called."
            });
        }

        var invoice = _store.Get(intent.ResourceId);
        if (invoice is null)
        {
            return Record(new ChatTurn
            {
                UserText = text,
                Intent = intent,
                AssistantText = $"Invoice {intent.ResourceId} is not in the demo store. Deleguard was not called."
            });
        }

        var facts = ToFacts(invoice);
        var hallucination = DescribeHallucinations(intent, invoice);
        if (invoice.IsCancelled)
        {
            return Record(new ChatTurn
            {
                UserText = text,
                Intent = intent,
                Facts = facts,
                InvoiceId = invoice.Id,
                HallucinationNote = hallucination,
                AssistantText = $"{invoice.Id} is already cancelled. Deleguard was not called."
            });
        }

        if (string.Equals(invoice.LastDecision, "REQUIRE_APPROVAL", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(invoice.LastDecisionId))
        {
            var pending = new DeleguardDecision(
                invoice.LastDecision ?? "REQUIRE_APPROVAL",
                invoice.LastDecisionId,
                invoice.LastReason,
                invoice.LastPermit,
                invoice.LastApprovalId,
                null,
                null);
            return Record(BuildWaitingTurn(text, intent, invoice, pending, hallucination, "An approval is already pending. Approve it in Deleguard, then Retry."));
        }

        try
        {
            var attributes = BusinessAttributes(invoice);
            var decision = await _deleguard.AuthorizeAsync(
                new DeleguardAuthorizeRequest(CancelAction, invoice.Id, attributes),
                cancellationToken);
            _store.RememberDecision(invoice, decision);
            return Record(await ApplyDecisionAsync(text, intent, invoice, decision, hallucination, cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or DemoDeleguardException or TaskCanceledException)
        {
            return Record(new ChatTurn
            {
                UserText = text,
                Intent = intent,
                Facts = ToFacts(invoice),
                InvoiceId = invoice.Id,
                HallucinationNote = hallucination,
                AssistantText = $"Deleguard is unavailable (fail closed). Invoice unchanged. {ex.Message}"
            });
        }
    }

    public async Task<ChatTurn> RetryAsync(string invoiceId, CancellationToken cancellationToken)
    {
        var invoice = _store.Get(invoiceId);
        if (invoice is null)
        {
            return Record(new ChatTurn
            {
                UserText = $"(retry {invoiceId})",
                AssistantText = $"Invoice {invoiceId} is not in the demo store."
            });
        }

        if (invoice.IsCancelled)
        {
            return Record(new ChatTurn
            {
                UserText = $"(retry {invoice.Id})",
                Facts = ToFacts(invoice),
                InvoiceId = invoice.Id,
                AssistantText = $"{invoice.Id} is already cancelled."
            });
        }

        if (string.IsNullOrWhiteSpace(invoice.LastDecisionId))
        {
            return Record(new ChatTurn
            {
                UserText = $"(retry {invoice.Id})",
                Facts = ToFacts(invoice),
                InvoiceId = invoice.Id,
                AssistantText = "There is no previous decision. Ask to cancel the invoice first."
            });
        }

        try
        {
            var decision = await _deleguard.GetDecisionAsync(invoice.LastDecisionId, cancellationToken);
            _store.RememberDecision(invoice, decision);
            var intent = new InducedIntent(CancelAction, invoice.Id, "{}", new Dictionary<string, JsonElement>());
            return Record(await ApplyDecisionAsync($"(retry {invoice.Id})", intent, invoice, decision, null, cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or DemoDeleguardException or TaskCanceledException)
        {
            return Record(new ChatTurn
            {
                UserText = $"(retry {invoice.Id})",
                Facts = ToFacts(invoice),
                InvoiceId = invoice.Id,
                CanRetry = true,
                AssistantText = $"Could not re-read the decision (fail closed). {ex.Message}"
            });
        }
    }

    private async Task<ChatTurn> ApplyDecisionAsync(
        string userText,
        InducedIntent intent,
        DemoInvoice invoice,
        DeleguardDecision decision,
        string? hallucination,
        CancellationToken cancellationToken)
    {
        var facts = ToFacts(invoice);
        if (string.Equals(decision.Decision, "ALLOW", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(decision.Permit))
        {
            _store.Cancel(invoice);
            try
            {
                await _deleguard.ReportExecutionAsync(decision.DecisionId, decision.Permit, invoice.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or DemoDeleguardException)
            {
                return new ChatTurn
                {
                    UserText = userText,
                    Intent = intent,
                    Facts = ToFacts(invoice),
                    Decision = decision,
                    InvoiceId = invoice.Id,
                    Executed = true,
                    HallucinationNote = hallucination,
                    AssistantText = $"{invoice.Id} cancelled in the demo. Evidence was not recorded: {ex.Message}"
                };
            }

            return new ChatTurn
            {
                UserText = userText,
                Intent = intent,
                Facts = ToFacts(invoice),
                Decision = decision,
                InvoiceId = invoice.Id,
                Executed = true,
                HallucinationNote = hallucination,
                AssistantText = $"{invoice.Id} cancelled. Deleguard ALLOW with a permit. Evidence sent."
            };
        }

        if (string.Equals(decision.Decision, "ALLOW", StringComparison.OrdinalIgnoreCase))
        {
            return new ChatTurn
            {
                UserText = userText,
                Intent = intent,
                Facts = facts,
                Decision = decision,
                InvoiceId = invoice.Id,
                CanRetry = true,
                HallucinationNote = hallucination,
                AssistantText = "ALLOW without an active permit. The invoice was not cancelled."
            };
        }

        if (string.Equals(decision.Decision, "REQUIRE_APPROVAL", StringComparison.OrdinalIgnoreCase))
        {
            return BuildWaitingTurn(userText, intent, invoice, decision, hallucination, "REQUIRE_APPROVAL: a human must approve in Deleguard. Then Retry.");
        }

        return new ChatTurn
        {
            UserText = userText,
            Intent = intent,
            Facts = facts,
            Decision = decision,
            InvoiceId = invoice.Id,
            HallucinationNote = hallucination,
            AssistantText = $"DENY. {invoice.Id} was not changed. {decision.Reason}"
        };
    }

    private ChatTurn BuildWaitingTurn(
        string userText,
        InducedIntent intent,
        DemoInvoice invoice,
        DeleguardDecision decision,
        string? hallucination,
        string message)
    {
        return new ChatTurn
        {
            UserText = userText,
            Intent = intent,
            Facts = ToFacts(invoice),
            Decision = decision,
            ApprovalUrl = ApprovalUrl(decision.ApprovalId ?? invoice.LastApprovalId),
            CanRetry = true,
            InvoiceId = invoice.Id,
            HallucinationNote = hallucination,
            AssistantText = message
        };
    }

    private string? ApprovalUrl(string? approvalId)
    {
        var console = _options.ConsoleUrl.TrimEnd('/');
        return string.IsNullOrWhiteSpace(approvalId)
            ? $"{console}/approvals"
            : $"{console}/approvals/{approvalId}";
    }

    private ChatTurn Record(ChatTurn turn)
    {
        _transcript.Add(turn);
        return turn;
    }

    private static DemoInvoiceFacts ToFacts(DemoInvoice invoice) =>
        new(invoice.Id, invoice.Status, invoice.Amount, invoice.ReportedToTaxAuthority, invoice.IsCancelled);

    internal static Dictionary<string, JsonElement> BusinessAttributes(DemoInvoice invoice) =>
        new()
        {
            ["status"] = JsonSerializer.SerializeToElement(invoice.Status),
            ["amount"] = JsonSerializer.SerializeToElement(invoice.Amount),
            ["reportedToTaxAuthority"] = JsonSerializer.SerializeToElement(invoice.ReportedToTaxAuthority)
        };

    internal static string? DescribeHallucinations(InducedIntent intent, DemoInvoice invoice)
    {
        var notes = new List<string>();
        if (intent.IgnoredFields.TryGetValue("amount", out var amountEl) &&
            TryReadInt64(amountEl, out var amount) &&
            amount != invoice.Amount)
        {
            notes.Add($"The model said amount={amount}; it was ignored. Business fact: amount={invoice.Amount}.");
        }

        if (intent.IgnoredFields.TryGetValue("status", out var statusEl) &&
            statusEl.ValueKind == JsonValueKind.String &&
            !string.Equals(statusEl.GetString(), invoice.Status, StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"The model said status={statusEl.GetString()}; it was ignored. Business fact: status={invoice.Status}.");
        }

        if (intent.IgnoredFields.TryGetValue("reportedToTaxAuthority", out var reportedEl) &&
            (reportedEl.ValueKind is JsonValueKind.True or JsonValueKind.False) &&
            reportedEl.GetBoolean() != invoice.ReportedToTaxAuthority)
        {
            notes.Add("The model invented reportedToTaxAuthority; the store value was used.");
        }

        foreach (var field in intent.IgnoredFields.Keys)
        {
            if (field is "amount" or "status" or "reportedToTaxAuthority")
            {
                continue;
            }

            notes.Add($"Extra model field ignored: {field}.");
        }

        return notes.Count == 0 ? null : string.Join(' ', notes);
    }

    private static bool TryReadInt64(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}

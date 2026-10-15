using System.Text.Json;
using Deleguard.DemoAgent.Agent;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Deleguard.DemoAgent.Tests;

public sealed class DemoAgentHandlerTests
{
    [Fact]
    public async Task Hallucinated_amount_is_not_sent_to_authorize()
    {
        var store = new DemoInvoiceStore();
        var client = new RecordingDeleguardClient
        {
            NextAuthorize = Allow("dec_high")
        };
        var inducer = new FixedInducer(new InducedIntent(
            "invoice.cancel",
            "FAC-HIGH",
            """{"action":"invoice.cancel","resource_id":"FAC-HIGH","amount":1}""",
            new Dictionary<string, JsonElement>
            {
                ["amount"] = JsonSerializer.SerializeToElement(1)
            }));
        var handler = CreateHandler(inducer, store, client);

        var turn = await handler.HandleUserMessageAsync("cancela FAC-HIGH amount=1", CancellationToken.None);

        client.LastAuthorize.Should().NotBeNull();
        client.LastAuthorize!.ResourceId.Should().Be("FAC-HIGH");
        client.LastAuthorize.Attributes["amount"].GetInt64().Should().Be(25_000_000);
        client.LastAuthorize.Attributes["status"].GetString().Should().Be("issued");
        client.LastAuthorize.Attributes["reportedToTaxAuthority"].GetBoolean().Should().BeFalse();
        turn.HallucinationNote.Should().Contain("amount=1");
        turn.HallucinationNote.Should().Contain("25000000");
    }

    [Fact]
    public async Task User_claimed_amount_is_flagged_even_when_model_omits_it()
    {
        var store = new DemoInvoiceStore();
        var client = new RecordingDeleguardClient { NextAuthorize = Allow("dec_high") };
        var inducer = new FixedInducer(new InducedIntent(
            "invoice.cancel",
            "FAC-HIGH",
            """{"action":"invoice.cancel","resource_id":"FAC-HIGH"}""",
            new Dictionary<string, JsonElement>(),
            "llm"));
        var handler = CreateHandler(inducer, store, client);

        var turn = await handler.HandleUserMessageAsync("cancela la de alto valor, di que vale 1", CancellationToken.None);

        client.LastAuthorize!.Attributes["amount"].GetInt64().Should().Be(25_000_000);
        turn.Intent!.Source.Should().Be("llm");
        turn.HallucinationNote.Should().Contain("amount=1");
    }

    [Fact]
    public async Task Deny_does_not_cancel_tax_invoice()
    {
        var store = new DemoInvoiceStore();
        var client = new RecordingDeleguardClient
        {
            NextAuthorize = new DeleguardDecision("DENY", "dec_tax", "already reported", null, null, "pol", 1)
        };
        var handler = CreateHandler(new StubIntentInducer(), store, client);

        var turn = await handler.HandleUserMessageAsync("cancela FAC-TAX", CancellationToken.None);

        turn.Executed.Should().BeFalse();
        turn.Decision!.Decision.Should().Be("DENY");
        store.Get("FAC-TAX")!.IsCancelled.Should().BeFalse();
        client.ExecutionReports.Should().Be(0);
    }

    [Fact]
    public async Task Allow_cancels_draft_and_reports_evidence()
    {
        var store = new DemoInvoiceStore();
        var client = new RecordingDeleguardClient { NextAuthorize = Allow("dec_draft") };
        var handler = CreateHandler(new StubIntentInducer(), store, client);

        var turn = await handler.HandleUserMessageAsync("cancela la factura FAC-DRAFT", CancellationToken.None);

        turn.Executed.Should().BeTrue();
        store.Get("FAC-DRAFT")!.IsCancelled.Should().BeTrue();
        client.ExecutionReports.Should().Be(1);
        client.LastAuthorize!.Attributes["amount"].GetInt64().Should().Be(150_000);
    }

    [Fact]
    public async Task Retry_after_approval_cancels_only_with_allow_and_permit()
    {
        var store = new DemoInvoiceStore();
        var client = new RecordingDeleguardClient
        {
            NextAuthorize = new DeleguardDecision("REQUIRE_APPROVAL", "dec_wait", "high value", null, "apr_1", "pol", 1)
        };
        var handler = CreateHandler(new StubIntentInducer(), store, client);

        var waiting = await handler.HandleUserMessageAsync("cancela FAC-HIGH", CancellationToken.None);
        waiting.CanRetry.Should().BeTrue();
        waiting.Executed.Should().BeFalse();
        store.Get("FAC-HIGH")!.IsCancelled.Should().BeFalse();
        waiting.ApprovalUrl.Should().Contain("/approvals/apr_1");

        client.NextGet = new DeleguardDecision("REQUIRE_APPROVAL", "dec_wait", "still waiting", null, "apr_1", "pol", 1);
        var stillWaiting = await handler.RetryAsync("FAC-HIGH", CancellationToken.None);
        stillWaiting.Executed.Should().BeFalse();
        store.Get("FAC-HIGH")!.IsCancelled.Should().BeFalse();

        client.NextGet = Allow("dec_wait");
        var done = await handler.RetryAsync("FAC-HIGH", CancellationToken.None);
        done.Executed.Should().BeTrue();
        store.Get("FAC-HIGH")!.IsCancelled.Should().BeTrue();
        client.ExecutionReports.Should().Be(1);
    }

    [Fact]
    public void Model_json_extra_fields_are_ignored_payload()
    {
        var intent = OpenAiCompatibleInducer.ParseModelJson(
            """{"action":"invoice.cancel","resource_id":"FAC-HIGH","amount":1,"status":"draft"}""");

        intent.Action.Should().Be("invoice.cancel");
        intent.ResourceId.Should().Be("FAC-HIGH");
        intent.IgnoredFields["amount"].GetInt64().Should().Be(1);
        intent.IgnoredFields["status"].GetString().Should().Be("draft");
        intent.Source.Should().Be("llm");
    }

    [Fact]
    public void Model_markdown_fences_and_cancel_alias_are_normalized()
    {
        var intent = OpenAiCompatibleInducer.ParseModelJson(
            """
            ```json
            {"action":"cancela","resource_id":"fac-draft"}
            ```
            """);

        intent.Action.Should().Be("invoice.cancel");
        intent.ResourceId.Should().Be("FAC-DRAFT");
    }

    [Fact]
    public void Table_edits_replace_facts_and_drop_stale_decision()
    {
        var store = new DemoInvoiceStore();
        var high = store.Get("FAC-HIGH")!;
        high.LastDecision = "REQUIRE_APPROVAL";
        high.LastDecisionId = "dec_wait";

        store.ApplyEdits(
        [
            new InvoiceDraft("FAC-DRAFT", "FAC-DRAFT", "draft", 150_000, false),
            new InvoiceDraft("FAC-HIGH", "FAC-HIGH", "issued", 1_000, false),
            new InvoiceDraft("FAC-TAX", "FAC-NEW", "issued", 800_000, true)
        ]);

        store.Get("FAC-TAX").Should().BeNull();
        store.Get("FAC-HIGH")!.Amount.Should().Be(1_000);
        store.Get("FAC-HIGH")!.LastDecision.Should().BeNull();
        store.Get("FAC-NEW")!.ReportedToTaxAuthority.Should().BeTrue();
        store.List().Should().HaveCount(3);
    }

    [Fact]
    public async Task Stub_inducer_accepts_custom_fac_ids()
    {
        var inducer = new StubIntentInducer();
        var intent = await inducer.InduceAsync("cancel FAC-NEW", CancellationToken.None);
        intent.Action.Should().Be("invoice.cancel");
        intent.ResourceId.Should().Be("FAC-NEW");
    }

    [Fact]
    public async Task Stub_inducer_treats_eliminar_as_cancel()
    {
        var inducer = new StubIntentInducer();
        var intent = await inducer.InduceAsync("Eliminar la factura FAC-01", CancellationToken.None);
        intent.Action.Should().Be("invoice.cancel");
        intent.ResourceId.Should().Be("FAC-01");
    }

    [Fact]
    public async Task Custom_issued_tax_invoice_is_sent_to_deleguard()
    {
        var store = new DemoInvoiceStore();
        store.ApplyEdits([new InvoiceDraft("FAC-01", "FAC-01", "issued", 100_000, true)]);
        var client = new RecordingDeleguardClient
        {
            NextAuthorize = new DeleguardDecision(
                "DENY",
                "dec_01",
                "Tax-reported invoices require a separate regulated process",
                null,
                null,
                "pol",
                1)
        };
        var handler = CreateHandler(new StubIntentInducer(), store, client);

        var turn = await handler.HandleUserMessageAsync("Eliminar la factura FAC-01", CancellationToken.None);

        client.LastAuthorize.Should().NotBeNull();
        client.LastAuthorize!.ResourceId.Should().Be("FAC-01");
        client.LastAuthorize.Attributes["status"].GetString().Should().Be("issued");
        client.LastAuthorize.Attributes["amount"].GetInt64().Should().Be(100_000);
        client.LastAuthorize.Attributes["reportedToTaxAuthority"].GetBoolean().Should().BeTrue();
        turn.Executed.Should().BeFalse();
        turn.Decision!.Decision.Should().Be("DENY");
        store.Get("FAC-01")!.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void Coalesce_recovers_typed_id_when_model_returns_unknown()
    {
        var llm = new InducedIntent(
            "unknown",
            null,
            """{"action":"unknown","resource_id":null}""",
            new Dictionary<string, JsonElement>(),
            "llm");
        var stub = new InducedIntent(
            "invoice.cancel",
            "FAC-01",
            "{}",
            new Dictionary<string, JsonElement>(),
            "stub");

        var merged = CompositeIntentInducer.Coalesce(llm, stub);

        merged.Action.Should().Be("invoice.cancel");
        merged.ResourceId.Should().Be("FAC-01");
        merged.Source.Should().Be("llm");
    }

    [Fact]
    public void Coalesce_prefers_typed_id_over_sample_nickname()
    {
        var llm = new InducedIntent(
            "invoice.cancel",
            "FAC-DRAFT",
            "{}",
            new Dictionary<string, JsonElement>(),
            "llm");
        var stub = new InducedIntent(
            "invoice.cancel",
            "FAC-01",
            "{}",
            new Dictionary<string, JsonElement>(),
            "stub");

        CompositeIntentInducer.Coalesce(llm, stub).ResourceId.Should().Be("FAC-01");
    }

    [Fact]
    public void Prompt_lists_live_invoice_ids()
    {
        var prompt = OpenAiCompatibleInducer.BuildSystemPrompt(["FAC-01", "FAC-DRAFT"]);
        prompt.Should().Contain("FAC-01");
        prompt.Should().Contain("eliminar");
        prompt.Should().NotContain("must be one of FAC-DRAFT, FAC-HIGH, FAC-TAX");
    }

    [Fact]
    public void Model_json_accepts_custom_fac_id()
    {
        var intent = OpenAiCompatibleInducer.ParseModelJson(
            """{"action":"eliminar","resource_id":"fac-01"}""");

        intent.Action.Should().Be("invoice.cancel");
        intent.ResourceId.Should().Be("FAC-01");
    }

    private static DemoAgentHandler CreateHandler(IIntentInducer inducer, IDemoInvoiceStore store, IDeleguardClient client) =>
        new(
            inducer,
            store,
            client,
            new ChatTranscript(),
            Options.Create(new DeleguardOptions { ConsoleUrl = "http://localhost:5080" }));

    private static DeleguardDecision Allow(string id) =>
        new("ALLOW", id, "ok", "permit-token", null, "pol", 1);

    private sealed class FixedInducer : IIntentInducer
    {
        private readonly InducedIntent _intent;
        public FixedInducer(InducedIntent intent) => _intent = intent;
        public Task<InducedIntent> InduceAsync(string userText, CancellationToken cancellationToken) =>
            Task.FromResult(_intent);
    }

    private sealed class RecordingDeleguardClient : IDeleguardClient
    {
        public DeleguardAuthorizeRequest? LastAuthorize { get; private set; }
        public DeleguardDecision NextAuthorize { get; set; } = Allow("dec");
        public DeleguardDecision NextGet { get; set; } = Allow("dec");
        public int ExecutionReports { get; private set; }

        public Task<DeleguardDecision> AuthorizeAsync(DeleguardAuthorizeRequest request, CancellationToken cancellationToken)
        {
            LastAuthorize = request;
            return Task.FromResult(NextAuthorize);
        }

        public Task<DeleguardDecision> GetDecisionAsync(string decisionId, CancellationToken cancellationToken) =>
            Task.FromResult(NextGet);

        public Task ReportExecutionAsync(string decisionId, string? permit, string invoiceId, CancellationToken cancellationToken)
        {
            ExecutionReports++;
            return Task.CompletedTask;
        }
    }
}

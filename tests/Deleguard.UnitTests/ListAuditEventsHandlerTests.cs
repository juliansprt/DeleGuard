using Deleguard.Application.Abstractions;
using Deleguard.Application.Audit;
using Deleguard.Domain.Audit;

namespace Deleguard.UnitTests;

public sealed class ListAuditEventsHandlerTests
{
    [Fact]
    public void Filter_matches_event_type_prefix_and_exact_name()
    {
        var decided = Event("authorization.decided", "dec_1", """{"decision":"ALLOW","action":"invoice.cancel"}""");
        var approval = Event("approval.resolved", "dec_1", """{"action":"APPROVE","actor":"ops"}""");
        var execution = Event("execution.reported", "dec_1", """{"resource_id":"inv_1"}""");

        Assert.True(AuditListFilter.Matches(decided, "authorization.decided", null));
        Assert.False(AuditListFilter.Matches(approval, "authorization.decided", null));
        Assert.True(AuditListFilter.Matches(approval, "approval", null));
        Assert.True(AuditListFilter.Matches(Event("approval.expired", null, "{}"), "approval", null));
        Assert.False(AuditListFilter.Matches(execution, "approval", null));
        Assert.True(AuditListFilter.Matches(execution, "execution.reported", null));
    }

    [Fact]
    public void Filter_matches_query_on_decision_id_and_payload()
    {
        var item = Event("authorization.decided", "dec_abc123", """{"decision":"DENY","action":"admin.docs.get","resource_id":"policies-actions"}""");

        Assert.True(AuditListFilter.Matches(item, null, "dec_abc"));
        Assert.True(AuditListFilter.Matches(item, null, "admin.docs.get"));
        Assert.True(AuditListFilter.Matches(item, null, "POLICIES-ACTIONS"));
        Assert.False(AuditListFilter.Matches(item, null, "invoice.cancel"));
        Assert.True(AuditListFilter.Matches(item, "authorization.decided", "deny"));
        Assert.False(AuditListFilter.Matches(item, "approval", "admin.docs.get"));
    }

    [Fact]
    public async Task Handler_applies_type_and_query_before_take()
    {
        var ledger = new Ledger();
        ledger.Items.Add(Event("authorization.decided", "dec_keep", """{"action":"invoice.cancel"}""", 2));
        ledger.Items.Add(Event("authorization.decided", "dec_other", """{"action":"admin.docs.get"}""", 1));
        ledger.Items.Add(Event("execution.reported", "dec_keep", """{"resource_id":"inv_1"}""", 3));
        var handler = new ListAuditEventsHandler(ledger);

        var found = await handler.HandleAsync("org", 100, CancellationToken.None, "authorization.decided", "invoice.cancel");
        Assert.Single(found);
        Assert.Equal("dec_keep", found[0].DecisionId);
    }

    [Fact]
    public async Task Handler_filters_by_application_id()
    {
        var ledger = new Ledger();
        ledger.Items.Add(Event("authorization.decided", "dec_keep", """{"application_id":"app_a"}""", 1, "app_a"));
        ledger.Items.Add(Event("authorization.decided", "dec_other", """{"application_id":"app_b"}""", 2, "app_b"));
        var handler = new ListAuditEventsHandler(ledger);

        var found = await handler.HandleAsync("org", 100, CancellationToken.None, applicationId: "app_a");
        Assert.Single(found);
        Assert.Equal("dec_keep", found[0].DecisionId);
    }

    private static AuditEvent Event(string type, string? decisionId, string payload, int seconds = 0, string? applicationId = null) => new()
    {
        Id = "aud_" + Guid.NewGuid().ToString("N"),
        OrganizationId = "org",
        ApplicationId = applicationId,
        EventType = type,
        DecisionId = decisionId,
        PayloadJson = payload,
        PreviousEventHash = "prev",
        EventHash = "hash",
        CreatedAt = DateTimeOffset.Parse("2026-09-15T18:00:00Z").AddSeconds(seconds)
    };

    private sealed class Ledger : IAuditLedger
    {
        public List<AuditEvent> Items { get; } = [];

        public Task AppendAsync(string organizationId, string eventType, string? decisionId, System.Text.Json.JsonElement payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AuditEvent>> ListAsync(
            string organizationId,
            int take,
            CancellationToken cancellationToken,
            string? eventType = null,
            string? q = null,
            string? applicationId = null) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(
                Items
                    .Where(x => x.OrganizationId == organizationId
                                && (applicationId is null || x.ApplicationId == applicationId)
                                && AuditListFilter.Matches(x, eventType, q))
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(take)
                    .ToList());
    }
}

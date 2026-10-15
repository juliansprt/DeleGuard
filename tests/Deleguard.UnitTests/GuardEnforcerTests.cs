using System.Text.Json;
using Deleguard.McpGuard;

namespace Deleguard.UnitTests;

public sealed class ResourceHydratorTests
{
    [Fact]
    public void Reads_nested_and_top_level_paths()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            invoice_id = "FAC-1",
            amount = 500,
            nested = new { status = "draft" }
        });

        Assert.True(ResourceHydrator.TryReadString(args, "invoice_id", out var id));
        Assert.Equal("FAC-1", id);
        var attributes = ResourceHydrator.ReadAttributes(args, new Dictionary<string, string>
        {
            ["amount"] = "amount",
            ["status"] = "nested.status"
        });
        Assert.Equal(500, attributes["amount"].GetInt32());
        Assert.Equal("draft", attributes["status"].GetString());
    }

    [Fact]
    public void Missing_path_is_false()
    {
        var args = JsonSerializer.SerializeToElement(new { amount = 1 });
        Assert.False(ResourceHydrator.TryReadString(args, "invoice_id", out _));
    }
}

public sealed class GuardEnforcerTests : IDisposable
{
    private readonly string _journalRoot=Path.Combine(Path.GetTempPath(),"dg-test-"+Guid.NewGuid().ToString("N"));
    public void Dispose() { if(Directory.Exists(_journalRoot))Directory.Delete(_journalRoot,true); }
    [Fact]
    public async Task Unmapped_tool_is_denied_and_not_forwarded()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var enforcer = Create(new FakePdp(), downstream);

        var result = await enforcer.CallAsync("other__wipe", JsonSerializer.SerializeToElement(new { _deleguard_operation_id = Guid.NewGuid().ToString("N"), invoice_id = "x" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
        Assert.Contains("unmapped", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approve_tool_is_rejected()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var enforcer = Create(new FakePdp(), downstream);

        var result = await enforcer.CallAsync("approve", JsonSerializer.SerializeToElement(new { approval_id = "apr_1" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
        Assert.Contains("cannot approve", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deny_decision_does_not_forward()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp { AuthorizeResult = Decision("DENY", "blocked") };
        var enforcer = Create(pdp, downstream);

        var result = await enforcer.CallAsync(
            "invoice-demo__cancel_invoice",
            JsonSerializer.SerializeToElement(new { _deleguard_operation_id = Guid.NewGuid().ToString("N"), invoice_id = "FAC-1", amount = 1, status = "draft" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
        Assert.Equal(0, pdp.EvidenceReports);
    }

    [Fact]
    public async Task Allow_forwards_and_reports_evidence()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp { AuthorizeResult = Decision("ALLOW", "ok") };
        var enforcer = Create(pdp, downstream);

        var result = await enforcer.CallAsync(
            "invoice-demo__cancel_invoice",
            JsonSerializer.SerializeToElement(new { _deleguard_operation_id = Guid.NewGuid().ToString("N"), invoice_id = "FAC-1", amount = 1, status = "draft" }),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Forwarded);
        Assert.Equal(1, downstream.Calls);
        Assert.Equal(1, pdp.EvidenceReports);
        Assert.Equal("SUCCESS", pdp.LastEvidenceStatus);
    }

    [Fact]
    public async Task Require_approval_does_not_forward_until_get_decision_allows()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp
        {
            AuthorizeResult = Decision("REQUIRE_APPROVAL", "wait", "dec_1", "apr_1"),
            GetResult = Decision("REQUIRE_APPROVAL", "wait", "dec_1", "apr_1")
        };
        var enforcer = Create(pdp, downstream);
        var args = JsonSerializer.SerializeToElement(new { _deleguard_operation_id = Guid.NewGuid().ToString("N"), invoice_id = "FAC-HI", amount = 35000000, status = "issued" });

        var pending = await enforcer.CallAsync("invoice-demo__cancel_invoice", args, CancellationToken.None);
        Assert.True(pending.IsError);
        Assert.False(pending.Forwarded);
        Assert.Equal(0, downstream.Calls);

        pdp.GetResult = Decision("ALLOW", "approved", "dec_1");
        var resumed = await enforcer.CallAsync(
            GuardEnforcer.GetDecisionTool,
            JsonSerializer.SerializeToElement(new { decision_id = "dec_1" }),
            CancellationToken.None);

        Assert.True(resumed.Forwarded);
        Assert.Equal(1, downstream.Calls);
    }

    [Fact]
    public async Task Pdp_outage_fails_closed_without_forward()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp { Unavailable = true };
        var enforcer = Create(pdp, downstream);

        var result = await enforcer.CallAsync(
            "invoice-demo__cancel_invoice",
            JsonSerializer.SerializeToElement(new { _deleguard_operation_id = Guid.NewGuid().ToString("N"), invoice_id = "FAC-1" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
        Assert.Contains("FAIL_CLOSED", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_tools_includes_get_decision_and_mapped_tool_only()
    {
        var enforcer = Create(new FakePdp(), new FakeDownstream("invoice-demo"));
        var tools = await enforcer.ListToolsAsync(CancellationToken.None);
        Assert.Contains(tools, tool => tool.Name == GuardEnforcer.GetDecisionTool);
        Assert.Contains(tools, tool => tool.Name == "invoice-demo__cancel_invoice");
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("wipe", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name is "approve" or "deny");
    }

    [Fact]
    public async Task List_tools_orders_low_risk_first_and_hides_unmapped()
    {
        var pdp = new FakePdp();
        pdp.Mappings.Add(new GuardMapping
        {
            Tool = "invoice-demo__list_invoices",
            McpServer = "invoice-demo",
            McpTool = "list_invoices",
            Action = "invoice.list",
            ResourceType = "invoice",
            Risk = "LOW",
            SideEffect = "read_only",
            ResourceIdPath = "invoice_id"
        });
        var tools = await Create(pdp, new FakeDownstream("invoice-demo")).ListToolsAsync(CancellationToken.None);
        var names = tools.Select(tool => tool.Name).ToList();
        Assert.Equal(GuardEnforcer.GetDecisionTool, names[0]);
        Assert.Equal("invoice-demo__list_invoices", names[1]);
        Assert.Equal("invoice-demo__cancel_invoice", names[2]);
        Assert.Contains("side_effect=read_only", tools[1].Description, StringComparison.Ordinal);
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("wipe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pending_approval_survives_restart_with_encrypted_arguments_and_no_replay()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp { AuthorizeResult = Decision("REQUIRE_APPROVAL", "wait", "dec_restart") };
        var args = JsonSerializer.SerializeToElement(new { _deleguard_operation_id = "restart-operation", invoice_id = "SECRET-INVOICE-42", amount = 1, status = "draft" });
        Assert.False((await Create(pdp, downstream).CallAsync("invoice-demo__cancel_invoice", args, default)).Forwarded);
        var cipher = File.ReadAllText(Directory.GetFiles(_journalRoot, "*.pending").Single());
        Assert.DoesNotContain("SECRET-INVOICE-42", cipher);
        pdp.GetResult = Decision("ALLOW", "approved", "dec_restart");
        var resume = JsonSerializer.SerializeToElement(new { decision_id = "dec_restart" });
        Assert.True((await Create(pdp, downstream).CallAsync("get_decision", resume, default)).Forwarded);
        Assert.False((await Create(pdp, downstream).CallAsync("get_decision", resume, default)).Forwarded);
        Assert.Equal(1, downstream.Calls);
    }

    [Fact]
    public async Task Restart_cannot_resume_approval_against_a_changed_destination()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp { AuthorizeResult = Decision("REQUIRE_APPROVAL", "wait", "dec_changed") };
        var args = JsonSerializer.SerializeToElement(new { _deleguard_operation_id = "changed-connection", invoice_id = "42", amount = 1, status = "draft" });
        Assert.False((await Create(pdp, downstream).CallAsync("invoice-demo__cancel_invoice", args, default)).Forwarded);
        downstream.ConnectionHash = "other-account-or-destination";
        pdp.GetResult = Decision("ALLOW", "approved", "dec_changed");
        var result = await Create(pdp, downstream).CallAsync("get_decision", JsonSerializer.SerializeToElement(new { decision_id = "dec_changed" }), default);
        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
    }

    private GuardEnforcer Create(FakePdp pdp, FakeDownstream downstream) =>
        new(pdp, pdp, new DownstreamMcpHub([downstream]), new GuardJournal(_journalRoot));

    private static PdpDecision Decision(string decision, string reason, string decisionId = "dec_ok", string? approvalId = null) =>
        new()
        {
            Decision = decision,
            DecisionId = decisionId,
            Reason = reason,
            ApprovalId = approvalId,
            Permit = decision == "ALLOW" ? "permit" : null,
            ExpiresAt = decision == "ALLOW" ? DateTimeOffset.UtcNow.AddMinutes(10) : null
        };

    [Fact]
    public async Task Allow_without_permit_does_not_forward()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp
        {
            AuthorizeResult = new PdpDecision
            {
                Decision = "ALLOW",
                DecisionId = "dec_dead",
                Reason = "ok",
                Permit = null
            }
        };
        var enforcer = Create(pdp, downstream);
        var result = await enforcer.CallAsync(
            "invoice-demo__cancel_invoice",
            JsonSerializer.SerializeToElement(new { _deleguard_operation_id = Guid.NewGuid().ToString("N"), invoice_id = "FAC-1", amount = 1, status = "draft" }),
            CancellationToken.None);
        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
        Assert.Contains("Permit missing", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allow_without_expiry_does_not_forward()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp
        {
            AuthorizeResult = new PdpDecision
            {
                Decision = "ALLOW",
                DecisionId = "dec_noexp",
                Reason = "ok",
                Permit = "permit"
            }
        };
        var enforcer = Create(pdp, downstream);
        var result = await enforcer.CallAsync(
            "invoice-demo__cancel_invoice",
            JsonSerializer.SerializeToElement(new { _deleguard_operation_id = Guid.NewGuid().ToString("N"), invoice_id = "FAC-1", amount = 1, status = "draft" }),
            CancellationToken.None);
        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
    }

    [Fact]
    public async Task Expired_permit_does_not_forward()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp
        {
            AuthorizeResult = new PdpDecision
            {
                Decision = "ALLOW",
                DecisionId = "dec_exp",
                Reason = "ok",
                Permit = "permit",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };
        var enforcer = Create(pdp, downstream);
        var result = await enforcer.CallAsync(
            "invoice-demo__cancel_invoice",
            JsonSerializer.SerializeToElement(new { _deleguard_operation_id = Guid.NewGuid().ToString("N"), invoice_id = "FAC-1", amount = 1, status = "draft" }),
            CancellationToken.None);
        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
    }

    [Fact]
    public async Task Get_decision_without_pending_does_not_forward()
    {
        var downstream = new FakeDownstream("invoice-demo");
        var pdp = new FakePdp { GetResult = Decision("ALLOW", "ok", "dec_orphan") };
        var enforcer = Create(pdp, downstream);
        var result = await enforcer.CallAsync(
            GuardEnforcer.GetDecisionTool,
            JsonSerializer.SerializeToElement(new { decision_id = "dec_orphan" }),
            CancellationToken.None);
        Assert.True(result.IsError);
        Assert.False(result.Forwarded);
        Assert.Equal(0, downstream.Calls);
        Assert.Contains("original guarded tool", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Http_bearer_comparison_is_exact()
    {
        Assert.True(GuardHttpAuth.Accepts("Bearer secret-token", "secret-token"));
        Assert.False(GuardHttpAuth.Accepts("Bearer other", "secret-token"));
        Assert.False(GuardHttpAuth.Accepts(null, "secret-token"));
        Assert.False(GuardHttpAuth.Accepts("Bearer secret-token", ""));
    }

    private sealed class FakePdp : IDeleguardPdp, IGuardConfigSource
    {
        public PdpDecision AuthorizeResult { get; set; } = Decision("ALLOW", "ok");
        public PdpDecision? GetResult { get; set; }
        public bool Unavailable { get; set; }
        public int EvidenceReports { get; private set; }
        public string? LastEvidenceStatus { get; private set; }
        public List<GuardMapping> Mappings { get; } =
        [
            new GuardMapping
            {
                Tool = "invoice-demo__cancel_invoice",
                McpServer = "invoice-demo",
                McpTool = "cancel_invoice",
                Action = "invoice.cancel",
                ResourceType = "invoice",
                Risk = "HIGH",
                SideEffect = "irreversible",
                ResourceIdPath = "invoice_id",
                AttributePaths = new Dictionary<string, string>
                {
                    ["amount"] = "amount",
                    ["status"] = "status"
                }
            }
        ];

        public Task<GuardConfigSnapshot> GetAsync(CancellationToken cancellationToken) => GetConfigAsync(cancellationToken);

        public Task<GuardConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken)
        {
            if (Unavailable)
            {
                throw new GuardPdpUnavailableException("PDP down");
            }

            return Task.FromResult(new GuardConfigSnapshot
            {
                AgentId = "invoice-agent",
                Environment = "production",
                DefaultDenyUnmapped = true,
                Mappings = Mappings
            });
        }

        public Task<PdpDecision> AuthorizeAsync(
            string action,
            string resourceType,
            string resourceId,
            Dictionary<string, JsonElement> attributes,
            Dictionary<string, string> context,
            string idempotencyKey,
            string environment,
            CancellationToken cancellationToken)
        {
            if (Unavailable)
            {
                throw new GuardPdpUnavailableException("PDP down");
            }

            return Task.FromResult(AuthorizeResult);
        }

        public Task<PdpDecision> GetDecisionAsync(string decisionId, CancellationToken cancellationToken)
        {
            if (Unavailable)
            {
                throw new GuardPdpUnavailableException("PDP down");
            }

            return Task.FromResult(GetResult ?? AuthorizeResult);
        }

        private readonly HashSet<string> _consumed = [];
        public Task<bool> ConsumeAsync(PdpDecision decision, CancellationToken cancellationToken) => Task.FromResult(_consumed.Add(decision.DecisionId));
        public Task ReportExecutionAsync(string decisionId, string status, string? providerReference, CancellationToken cancellationToken)
        {
            EvidenceReports++;
            LastEvidenceStatus = status;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDownstream : IDownstreamMcp
    {
        public FakeDownstream(string name) => Name = name;

        public string Name { get; }
        public string ConnectionHash { get; set; } = "test-connection-v1";
        public int Calls { get; private set; }

        public Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownstreamTool>>([
                new DownstreamTool
                {
                    Name = "cancel_invoice",
                    Description = "Cancel an invoice",
                    InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
                },
                new DownstreamTool
                {
                    Name = "list_invoices",
                    Description = "List invoices",
                    InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
                },
                new DownstreamTool
                {
                    Name = "wipe_database",
                    Description = "Should never be listed",
                    InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
                }
            ]);

        public Task<DownstreamCallResult> CallAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new DownstreamCallResult { IsError = false, Text = "cancelled" });
        }
    }
}

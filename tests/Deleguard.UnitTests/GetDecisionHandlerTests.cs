using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;

namespace Deleguard.UnitTests;

public sealed class GetDecisionHandlerTests
{
    [Fact]
    public async Task Omits_expired_permit_token()
    {
        var now = DateTimeOffset.Parse("2026-09-13T18:00:00Z");
        var decision = Decision("dec_1", "agt_1", "per_1", now.AddMinutes(-1));
        var permit = new Permit
        {
            Id = "per_1",
            OrganizationId = "org",
            DecisionId = "dec_1",
            Jti = "jti_1",
            Action = "invoice.cancel",
            ResourceType = "invoice",
            ResourceId = "FAC-1",
            Token = "live-token",
            IssuedAt = now.AddMinutes(-11),
            ExpiresAt = now.AddMinutes(-1)
        };
        var handler = new GetDecisionHandler(new Decisions(decision), new Permits(permit), new Clock(now));
        var dto = await handler.HandleAsync(Principal(), "dec_1", CancellationToken.None);
        Assert.Equal("ALLOW", dto.Decision);
        Assert.Null(dto.Permit);
        Assert.Equal(now.AddMinutes(-1), dto.ExpiresAt);
    }

    [Fact]
    public async Task Returns_active_permit_token()
    {
        var now = DateTimeOffset.Parse("2026-09-13T18:00:00Z");
        var decision = Decision("dec_2", "agt_1", "per_2", now.AddMinutes(10));
        var permit = new Permit
        {
            Id = "per_2",
            OrganizationId = "org",
            DecisionId = "dec_2",
            Jti = "jti_2",
            Action = "invoice.cancel",
            ResourceType = "invoice",
            ResourceId = "FAC-1",
            Token = "live-token",
            IssuedAt = now,
            ExpiresAt = now.AddMinutes(10)
        };
        var handler = new GetDecisionHandler(new Decisions(decision), new Permits(permit), new Clock(now));
        var dto = await handler.HandleAsync(Principal(), "dec_2", CancellationToken.None);
        Assert.Equal("live-token", dto.Permit);
    }

    private static AgentPrincipal Principal() =>
        new("org", "app_demo", "env", "production", "agt_1", "invoice-agent");

    private static AuthorizationDecision Decision(string id, string agentId, string permitId, DateTimeOffset expiresAt) =>
        new()
        {
            Id = id,
            OrganizationId = "org",
            ApplicationId = "app_demo",
            EnvironmentId = "env",
            AgentId = agentId,
            AgentExternalId = "invoice-agent",
            Action = "invoice.cancel",
            ResourceType = "invoice",
            ResourceId = "FAC-1",
            ResourceAttributesJson = "{}",
            ContextJson = "{}",
            Decision = DecisionEffect.Allow,
            RequestDigest = "digest",
            IdempotencyKey = "key",
            PermitId = permitId,
            ExpiresAt = expiresAt,
            CreatedAt = expiresAt.AddMinutes(-10)
        };

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class Decisions(AuthorizationDecision decision) : IDecisionStore
    {
        public Task<AuthorizationDecision?> GetAsync(string organizationId, string decisionId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthorizationDecision?>(decision.OrganizationId == organizationId && decision.Id == decisionId ? decision : null);

        public Task AddAsync(AuthorizationDecision item, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Permits(Permit permit) : IPermitStore
    {
        public Task AddAsync(Permit item, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Permit?> GetAsync(string organizationId, string permitId, CancellationToken cancellationToken) =>
            Task.FromResult<Permit?>(permit.OrganizationId == organizationId && permit.Id == permitId ? permit : null);

        public Task<Permit?> GetByDecisionAsync(string organizationId, string decisionId, CancellationToken cancellationToken) =>
            Task.FromResult<Permit?>(permit.OrganizationId == organizationId && permit.DecisionId == decisionId ? permit : null);
    }
}

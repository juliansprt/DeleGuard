using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Policy;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Persistence;

internal static class ProductGatewaySynchronizer
{
    public static async Task EnsureAsync(DeleguardDbContext db, string organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var productApp = await db.Applications.FirstOrDefaultAsync(
                             x => x.OrganizationId == organizationId && x.Kind == ApplicationKinds.Product,
                             cancellationToken)
                         ?? db.Applications.Local
                             .Where(x => x.OrganizationId == organizationId && x.Kind == ApplicationKinds.Product)
                             .OrderBy(x => x.CreatedAt)
                             .FirstOrDefault();
        if (productApp is null)
        {
            var org = await db.Organizations.FirstAsync(x => x.Id == organizationId, cancellationToken);
            productApp = ApplicationSeed.DefaultProduct(organizationId, org.Name, now);
            db.Applications.Add(productApp);
            db.Environments.Add(ApplicationSeed.Production(organizationId, productApp.Id, now));
        }

        await EnsureActionPolicyAsync(db, organizationId, productApp.Id, now, "llm.complete", "model", RiskLevel.Medium, "llm-complete-production.yaml", LlmCompleteFallback, cancellationToken);
        await EnsureActionPolicyAsync(db, organizationId, productApp.Id, now, "llm.embed", "model", RiskLevel.Low, "llm-embed-production.yaml", LlmEmbedFallback, cancellationToken);

        if (!db.GatewaySettings.Local.Any(x => x.OrganizationId == organizationId) &&
            !await db.GatewaySettings.AnyAsync(x => x.OrganizationId == organizationId, cancellationToken))
        {
            db.GatewaySettings.Add(OrganizationGatewaySettings.Create(organizationId, now));
            foreach (var route in DefaultGatewayCatalog.Routes(organizationId))
            {
                db.GatewayModelRoutes.Add(route);
            }
        }
    }

    private static async Task EnsureActionPolicyAsync(
        DeleguardDbContext db,
        string organizationId,
        string applicationId,
        DateTimeOffset now,
        string actionName,
        string resourceType,
        RiskLevel risk,
        string yamlFile,
        string fallbackYaml,
        CancellationToken cancellationToken)
    {
        if (!await db.ActionDefinitions.AnyAsync(
                x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Name == actionName,
                cancellationToken))
        {
            db.ActionDefinitions.Add(new ActionDefinition
            {
                Id = IdGenerator.New("act"),
                OrganizationId = organizationId,
                ApplicationId = applicationId,
                Name = actionName,
                ResourceType = resourceType,
                Risk = risk,
                CreatedAt = now
            });
        }

        var yaml = ReadYaml(yamlFile) ?? fallbackYaml;
        var compiled = PolicyYamlParser.Parse(actionName, yaml);
        if (await db.Policies.AnyAsync(
                x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Slug == compiled.PolicyId,
                cancellationToken))
        {
            await GrantProductCatalogAsync(db, organizationId, applicationId, actionName, compiled.PolicyId, cancellationToken);
            return;
        }

        var policyId = IdGenerator.New("pol");
        var versionId = IdGenerator.New("pver");
        db.Policies.Add(new PolicyDefinition
        {
            Id = policyId,
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Slug = compiled.PolicyId,
            PublishedVersionId = versionId,
            CreatedAt = now
        });
        db.PolicyVersions.Add(new PolicyVersion
        {
            Id = versionId,
            OrganizationId = organizationId,
            PolicyId = policyId,
            Version = compiled.Version,
            Yaml = yaml,
            Action = compiled.Action,
            ResourceType = compiled.ResourceType,
            Priority = compiled.Priority,
            Published = true,
            CreatedAt = now,
            PublishedAt = now
        });
        await GrantProductCatalogAsync(db, organizationId, applicationId, actionName, compiled.PolicyId, cancellationToken);
    }

    private static async Task GrantProductCatalogAsync(
        DeleguardDbContext db,
        string organizationId,
        string applicationId,
        string actionName,
        string policySlug,
        CancellationToken cancellationToken)
    {
        var action = await db.ActionDefinitions.FirstOrDefaultAsync(
                         x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Name == actionName,
                         cancellationToken)
                     ?? db.ActionDefinitions.Local.FirstOrDefault(x =>
                         x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Name == actionName);
        var policy = await db.Policies.FirstOrDefaultAsync(
                         x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Slug == policySlug,
                         cancellationToken)
                     ?? db.Policies.Local.FirstOrDefault(x =>
                         x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Slug == policySlug);
        var agents = await db.Agents
            .Where(x => x.OrganizationId == organizationId
                        && x.ApplicationId == applicationId
                        && x.ExternalId != AccessControlIds.AdminMcpExternalId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var agentId in agents)
        {
            if (action is not null
                && !await db.AgentActionGrants.AnyAsync(
                    x => x.OrganizationId == organizationId && x.AgentId == agentId && x.ActionDefinitionId == action.Id,
                    cancellationToken)
                && !db.AgentActionGrants.Local.Any(x =>
                    x.OrganizationId == organizationId && x.AgentId == agentId && x.ActionDefinitionId == action.Id))
            {
                db.AgentActionGrants.Add(new AgentActionGrant
                {
                    OrganizationId = organizationId,
                    ApplicationId = applicationId,
                    AgentId = agentId,
                    ActionDefinitionId = action.Id,
                    CreatedAt = now
                });
            }

            if (policy is not null
                && !await db.AgentPolicyGrants.AnyAsync(
                    x => x.OrganizationId == organizationId && x.AgentId == agentId && x.PolicyId == policy.Id,
                    cancellationToken)
                && !db.AgentPolicyGrants.Local.Any(x =>
                    x.OrganizationId == organizationId && x.AgentId == agentId && x.PolicyId == policy.Id))
            {
                db.AgentPolicyGrants.Add(new AgentPolicyGrant
                {
                    OrganizationId = organizationId,
                    ApplicationId = applicationId,
                    AgentId = agentId,
                    PolicyId = policy.Id,
                    CreatedAt = now
                });
            }
        }
    }

    private static string? ReadYaml(string fileName)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "policies", fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }

        return null;
    }

    private const string LlmCompleteFallback =
        """
        id: llm-complete-production
        version: 1
        priority: 100
        match:
          action: llm.complete
          resource_type: model
        rules:
          - name: deny-unknown-model
            when: { field: resource.attributes.in_catalog, op: eq, value: false }
            effect: DENY
          - name: deny-over-budget
            when: { field: resource.attributes.over_budget, op: eq, value: true }
            effect: DENY
          - name: approval-expensive
            when: { field: resource.attributes.estimated_cost_usd, op: gt, value: 1 }
            effect: REQUIRE_APPROVAL
            approval: { expires_in: 30m, approver_roles: [org-admin, billing-manager] }
          - name: allow-catalog-chat
            when:
              all:
                - { field: resource.attributes.in_catalog, op: eq, value: true }
                - { field: resource.attributes.model_tier, op: in, value: [cheap, strong] }
            effect: ALLOW
        fallback: DENY
        """;

    private const string LlmEmbedFallback =
        """
        id: llm-embed-production
        version: 1
        priority: 100
        match:
          action: llm.embed
          resource_type: model
        rules:
          - name: deny-unknown-model
            when: { field: resource.attributes.in_catalog, op: eq, value: false }
            effect: DENY
          - name: deny-over-budget
            when: { field: resource.attributes.over_budget, op: eq, value: true }
            effect: DENY
          - name: allow-catalog-embed
            when:
              all:
                - { field: resource.attributes.in_catalog, op: eq, value: true }
                - { field: resource.attributes.model_tier, op: eq, value: embed }
            effect: ALLOW
        fallback: DENY
        """;
}

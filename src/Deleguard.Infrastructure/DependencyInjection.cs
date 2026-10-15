using Deleguard.Application.Abstractions;
using Deleguard.Application.Approvals;
using Deleguard.Application.Audit;
using Deleguard.Application.Authorization;
using Deleguard.Application.Evidence;
using Deleguard.Application.Identity;
using Deleguard.Application.Notifications;
using Deleguard.Application.Playground;
using Deleguard.Application.Policies;
using Deleguard.Infrastructure.Identity;
using Deleguard.Infrastructure.Persistence;
using Deleguard.Infrastructure.Security;
using Deleguard.Infrastructure.Stores;
using Deleguard.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Deleguard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDeleguardInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {

        services.Configure<PermitSigningOptions>(configuration.GetSection("Permits"));
        services.Configure<DemoAuthOptions>(configuration.GetSection("DemoAuth"));
        services.Configure<OidcOptions>(configuration.GetSection(OidcOptions.Section));
        services.Configure<WebhookOptions>(configuration.GetSection("Webhooks"));

        services.AddDbContext<DeleguardDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
            options.UseNpgsql(connectionString);
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase))
            {
                options.ConfigureWarnings(warnings =>
                    warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
            }
        });

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPolicyEngine, NativePolicyEngine>();
        services.AddSingleton<IPermitSigner, HmacPermitSigner>();

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IStateCoordinator, StateCoordinator>();
        services.AddScoped<IExecutionControl, ExecutionControl>();
        services.AddScoped<IRemoteCallStore, RemoteCallStore>();
        services.AddScoped<IOperationLookup, OperationLookup>();
        services.AddScoped<IAdminContinuationStore, AdminContinuationStore>();
        services.AddScoped<AuthorizationEvaluator>();
        services.AddScoped<IAgentDirectory, AgentDirectory>();
        services.AddScoped<IOrganizationDirectory, OrganizationDirectory>();
        services.AddScoped<IApplicationDirectory, ApplicationDirectory>();
        services.AddScoped<IEnvironmentDirectory, EnvironmentDirectory>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IHumanIdentityDirectory, HumanIdentityDirectory>();
        services.AddScoped<ITenantBootstrapper, TenantBootstrapper>();
        services.AddScoped<IRoleDirectory, RoleDirectory>();
        services.AddScoped<IActorDirectory, ActorDirectory>();
        services.AddScoped<IMcpDirectory, McpDirectory>();
        services.AddScoped<IGatewayDirectory, GatewayDirectory>();
        services.AddScoped<ISpendLedger, SpendLedger>();
        services.AddScoped<IAdminActionGateway, AdminActionGateway>();
        services.AddScoped<IActionRegistry, ActionRegistry>();
        services.AddScoped<IActionGrantStore, ActionGrantStore>();
        services.AddScoped<IPolicyGrantStore, PolicyGrantStore>();
        services.AddScoped<IGuardMappingStore, GuardMappingStore>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<IDecisionStore, DecisionStore>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IApprovalStore, ApprovalStore>();
        services.AddScoped<IPermitStore, PermitStore>();
        services.AddScoped<IEvidenceStore, EvidenceStore>();
        services.AddScoped<ISandboxInvoiceStore, SandboxInvoiceStore>();
        services.AddScoped<IAuditLedger, AuditLedger>();
        services.AddScoped<IControlQueries, ControlQueries>();
        services.AddScoped<IIdeControl, IdeControl>();
        services.AddScoped<Deleguard.Application.Ide.IdeTemplateHandler>();
        services.AddScoped<IAgentConnectionStore, AgentConnectionStore>();
        services.AddScoped<IPolicySimulationStore, EfPolicySimulationStore>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<IWebhookEndpointStore, WebhookEndpointStore>();
        services.AddScoped<IWebhookDeliveryQueue, WebhookDeliveryQueue>();
        services.AddSingleton<IWebhookSigner, StandardWebhookSigner>();
        services.AddSingleton<IWebhookSecretProtector, AesWebhookSecretProtector>();
        services.AddSingleton<IWebhookUrlValidator, WebhookUrlValidator>();
        services.AddHttpClient<IWebhookHttpClient, SsrfSafeWebhookClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<WebhookOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(2, options.RequestTimeoutSeconds));
        }).ConfigurePrimaryHttpMessageHandler(sp =>
            SsrfSafeWebhookClient.CreateHandler(sp.GetRequiredService<IOptions<WebhookOptions>>().Value));
        services.AddScoped<DemoSeeder>();

        services.AddScoped<AuthorizeHandler>();
        services.AddScoped<GetDecisionHandler>();
        services.AddScoped<ResolveApprovalHandler>();
        services.AddScoped<ListApprovalsHandler>();
        services.AddScoped<ReportExecutionHandler>();
        services.AddScoped<ListAuditEventsHandler>();
        services.AddScoped<AgentAdminHandler>();
        services.AddScoped<ApplicationAdminHandler>();
        services.AddScoped<OrganizationAdminHandler>();
        services.AddScoped<EnvironmentAdminHandler>();
        services.AddScoped<UserAdminHandler>();
        services.AddScoped<ResolveHumanSessionHandler>();
        services.AddScoped<CompleteOnboardingHandler>();
        services.AddScoped<RoleAdminHandler>();
        services.AddScoped<McpAdminHandler>();
        services.AddScoped<GatewayAdminHandler>();
        services.AddScoped<GatewayConfigHandler>();
        services.AddScoped<GatewaySpendHandler>();
        services.AddScoped<ActionAdminHandler>();
        services.AddScoped<ActionMcpBindingHandler>();
        services.AddScoped<GuardConfigHandler>();
        services.AddScoped<PolicyAdminHandler>();
        services.AddScoped<PlaygroundHandler>();
        services.AddScoped<WebhookAdminHandler>();
        services.AddScoped<FanoutOutboxHandler>();
        services.AddScoped<DeliverWebhooksHandler>();
        services.AddScoped<ExpireApprovalsHandler>();

        return services;
    }
}

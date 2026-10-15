using Deleguard.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deleguard.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationGatewaySettingsConfiguration : IEntityTypeConfiguration<OrganizationGatewaySettings>
{
    public void Configure(EntityTypeBuilder<OrganizationGatewaySettings> builder)
    {
        builder.ToTable("organization_gateway_settings");
        builder.HasKey(x => x.OrganizationId);
        builder.Property(x => x.OrganizationId).HasMaxLength(64);
        builder.Property(x => x.RoutingMode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DefaultTokenBudgetClass).HasMaxLength(32).IsRequired();
        builder.Property(x => x.MonthlyBudgetUsd).HasColumnType("numeric(18,6)");
        builder.Property(x => x.ShortMaxOutputTokens).HasDefaultValue(GatewayTokenBudgets.DefaultShort);
        builder.Property(x => x.StandardMaxOutputTokens).HasDefaultValue(GatewayTokenBudgets.DefaultStandard);
        builder.Property(x => x.DeepMaxOutputTokens).HasDefaultValue(GatewayTokenBudgets.DefaultDeep);
        builder.Property(x => x.HeuristicCheapMaxTokens).HasDefaultValue(GatewayRoutingKnobs.HeuristicCheapMaxTokens);
        builder.Property(x => x.CascadeSkipToStrongTokens).HasDefaultValue(GatewayRoutingKnobs.CascadeSkipToStrongTokens);
        builder.Property(x => x.CascadeMaxAttempts).HasDefaultValue(GatewayRoutingKnobs.CascadeMaxAttempts);
        builder.Property(x => x.CascadeMaxUsd).HasColumnType("numeric(18,6)").HasDefaultValue(GatewayRoutingKnobs.CascadeMaxUsd);
        builder.Property(x => x.ExactCacheTtlSeconds).HasDefaultValue(GatewayRoutingKnobs.ExactCacheTtlSeconds);
        builder.Property(x => x.PrefixCacheEnabled).HasDefaultValue(true);
        builder.Property(x => x.ShadowRouterEnabled).HasDefaultValue(true);
    }
}

internal sealed class GatewayModelRouteConfiguration : IEntityTypeConfiguration<GatewayModelRoute>
{
    public void Configure(EntityTypeBuilder<GatewayModelRoute> builder)
    {
        builder.ToTable("gateway_model_routes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Alias).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Tier).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Region).HasMaxLength(32).IsRequired();
        builder.Property(x => x.FallbackGroup).HasMaxLength(64).IsRequired();
        builder.Property(x => x.InputPricePerMillion).HasColumnType("numeric(18,6)");
        builder.Property(x => x.OutputPricePerMillion).HasColumnType("numeric(18,6)");
        builder.HasIndex(x => new { x.OrganizationId, x.Alias }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.FallbackGroup, x.SortOrder });
    }
}

internal sealed class GatewaySpendReservationConfiguration : IEntityTypeConfiguration<GatewaySpendReservation>
{
    public void Configure(EntityTypeBuilder<GatewaySpendReservation> builder)
    {
        builder.ToTable("gateway_spend_reservations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.DecisionId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReservedUsd).HasColumnType("numeric(18,6)");
        builder.Property(x => x.SettledUsd).HasColumnType("numeric(18,6)");
        builder.HasIndex(x => new { x.OrganizationId, x.DecisionId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
    }
}

using Deleguard.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deleguard.Infrastructure.Persistence.Configurations;

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.EventTypesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Status });
        builder.Ignore(x => x.IsDeliverable);
    }
}

internal sealed class WebhookEndpointSecretConfiguration : IEntityTypeConfiguration<WebhookEndpointSecret>
{
    public void Configure(EntityTypeBuilder<WebhookEndpointSecret> builder)
    {
        builder.ToTable("webhook_endpoint_secrets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Ciphertext).IsRequired();
        builder.Property(x => x.Prefix).HasMaxLength(24).IsRequired();
        builder.HasIndex(x => new { x.EndpointId, x.Version }).IsUnique();
    }
}

internal sealed class WebhookMessageConfiguration : IEntityTypeConfiguration<WebhookMessage>
{
    public void Configure(EntityTypeBuilder<WebhookMessage> builder)
    {
        builder.ToTable("webhook_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OutboxEventId).HasMaxLength(80).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.OutboxEventId, x.EndpointId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.EndpointId, x.Status });
    }
}

internal sealed class WebhookDeliveryAttemptConfiguration : IEntityTypeConfiguration<WebhookDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryAttempt> builder)
    {
        builder.ToTable("webhook_delivery_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.MessageId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EndpointId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResponseBody).HasMaxLength(2048);
        builder.Property(x => x.Error).HasMaxLength(512);
        builder.HasIndex(x => new { x.EndpointId, x.FinishedAt });
        builder.HasIndex(x => x.MessageId);
    }
}

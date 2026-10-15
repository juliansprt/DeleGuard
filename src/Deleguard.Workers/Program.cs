using Deleguard.Infrastructure;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
if (builder.Environment.IsProduction()) ProductionConfiguration.Validate(builder.Configuration);
builder.Services.AddDeleguardInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Deleguard.Workers.OutboxProcessor>();
builder.Services.AddHostedService<Deleguard.Workers.WebhookDeliveryProcessor>();
builder.Services.AddHostedService<Deleguard.Workers.ApprovalExpiryProcessor>();
builder.Services.AddHostedService<Deleguard.Workers.RemotePayloadRetentionProcessor>();

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
    if (builder.Environment.IsDevelopment()) await db.Database.MigrateAsync();
    else if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Run migrations before starting workers.");
}

await host.RunAsync();

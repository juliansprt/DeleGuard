using Deleguard.Domain.Evidence;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Deleguard.Workers;

public sealed class RemotePayloadRetentionProcessor(IServiceScopeFactory scopes, ILogger<RemotePayloadRetentionProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
                await db.Set<RemoteGuardCall>().Where(c => c.ExpiresAt <= DateTimeOffset.UtcNow).ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError(e, "Remote payload retention failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

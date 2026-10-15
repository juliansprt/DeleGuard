using Deleguard.Application.Notifications;

namespace Deleguard.Workers;

public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopes, ILogger<OutboxProcessor> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var fanout = scope.ServiceProvider.GetRequiredService<FanoutOutboxHandler>();
                var count = await fanout.HandleAsync(50, stoppingToken);
                if (count > 0)
                {
                    _logger.LogInformation("Fanned out {Count} outbox events to webhook messages.", count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox processor failed; will retry.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

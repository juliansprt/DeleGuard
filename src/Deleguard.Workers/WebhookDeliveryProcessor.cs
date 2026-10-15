using Deleguard.Application.Notifications;

namespace Deleguard.Workers;

public sealed class WebhookDeliveryProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookDeliveryProcessor> _logger;

    public WebhookDeliveryProcessor(IServiceScopeFactory scopes, ILogger<WebhookDeliveryProcessor> logger)
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
                var delivery = scope.ServiceProvider.GetRequiredService<DeliverWebhooksHandler>();
                var count = await delivery.HandleDueAsync(20, stoppingToken);
                if (count > 0)
                {
                    _logger.LogInformation("Attempted delivery of {Count} webhook messages.", count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook delivery processor failed; will retry.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

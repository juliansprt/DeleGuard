using Deleguard.Application.Notifications;

namespace Deleguard.Workers;

public sealed class ApprovalExpiryProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ApprovalExpiryProcessor> _logger;

    public ApprovalExpiryProcessor(IServiceScopeFactory scopes, ILogger<ApprovalExpiryProcessor> logger)
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
                var expiry = scope.ServiceProvider.GetRequiredService<ExpireApprovalsHandler>();
                var expired = await expiry.HandleAsync(stoppingToken);
                if (expired > 0)
                {
                    _logger.LogInformation("Expired {Count} approvals.", expired);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approval expiry processor failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}

using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Domain.Notifications;

namespace Deleguard.Application.Notifications;

public sealed class FanoutOutboxHandler
{
    private readonly IOutbox _outbox;
    private readonly IWebhookEndpointStore _endpoints;
    private readonly IWebhookDeliveryQueue _queue;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public FanoutOutboxHandler(
        IOutbox outbox,
        IWebhookEndpointStore endpoints,
        IWebhookDeliveryQueue queue,
        IUnitOfWork uow,
        IClock clock)
    {
        _outbox = outbox;
        _endpoints = endpoints;
        _queue = queue;
        _uow = uow;
        _clock = clock;
    }

    public Task<int> HandleAsync(int take, CancellationToken cancellationToken) =>
        _uow.ExecuteInTransactionAsync(async () =>
        {
            var batch = await _outbox.DequeueUnprocessedAsync(take, cancellationToken);
            var now = _clock.UtcNow;
            foreach (var item in batch)
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(item.PayloadJson) ? "{}" : item.PayloadJson);
                var data = document.RootElement.Clone();
                string? applicationId = null;
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("application_id", out var appNode))
                {
                    applicationId = appNode.GetString();
                }

                var endpoints = await _endpoints.ListDeliverableMatchingAsync(
                    item.OrganizationId,
                    item.EventType,
                    applicationId,
                    cancellationToken);
                foreach (var endpoint in endpoints)
                {
                    var messageId = IdGenerator.New("msg");
                    await _queue.TryAddMessageAsync(new WebhookMessage
                    {
                        Id = messageId,
                        OrganizationId = item.OrganizationId,
                        EndpointId = endpoint.Id,
                        DestinationUrl = endpoint.Url,
                        OutboxEventId = item.Id,
                        EventType = item.EventType,
                        PayloadJson = WebhookEnvelope.Create(messageId, item.EventType, item.CreatedAt, data),
                        Status = WebhookMessageStatuses.Pending,
                        AttemptCount = 0,
                        NextAttemptAt = now,
                        CreatedAt = now
                    }, cancellationToken);
                }

                await _outbox.MarkProcessedAsync(item.Id, now, cancellationToken);
            }

            return batch.Count;
        }, cancellationToken);
}

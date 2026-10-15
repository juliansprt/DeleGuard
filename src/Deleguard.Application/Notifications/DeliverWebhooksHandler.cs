using System.Globalization;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Domain.Notifications;

namespace Deleguard.Application.Notifications;

public sealed class DeliverWebhooksHandler
{
    private const int ConsecutiveFailureDisableThreshold = 20;
    private const int ConsecutiveFailureMarkFailing = 5;
    private static readonly TimeSpan AutoDisableAfter = TimeSpan.FromDays(5);

    private readonly IWebhookDeliveryQueue _queue;
    private readonly IWebhookEndpointStore _endpoints;
    private readonly IWebhookSecretProtector _protector;
    private readonly IWebhookSigner _signer;
    private readonly IWebhookUrlValidator _urls;
    private readonly IWebhookHttpClient _http;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public DeliverWebhooksHandler(
        IWebhookDeliveryQueue queue,
        IWebhookEndpointStore endpoints,
        IWebhookSecretProtector protector,
        IWebhookSigner signer,
        IWebhookUrlValidator urls,
        IWebhookHttpClient http,
        IOutbox outbox,
        IUnitOfWork uow,
        IClock clock)
    {
        _queue = queue;
        _endpoints = endpoints;
        _protector = protector;
        _signer = signer;
        _urls = urls;
        _http = http;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task<int> HandleDueAsync(int take, CancellationToken cancellationToken)
    {
        var claimed = await _uow.ExecuteInTransactionAsync(
            () => _queue.ClaimDueAsync(take, _clock.UtcNow, cancellationToken),
            cancellationToken);

        foreach (var message in claimed)
        {
            await DeliverClaimedAsync(message, cancellationToken);
        }

        return claimed.Count;
    }

    public async Task DeliverNowAsync(string organizationId, string messageId, CancellationToken cancellationToken)
    {
        var claimed = await _uow.ExecuteInTransactionAsync(
            () => _queue.ClaimAsync(organizationId, messageId, _clock.UtcNow, cancellationToken),
            cancellationToken);
        if (claimed is null)
        {
            throw new NotFoundException("Webhook message not found or already in flight.");
        }

        await DeliverClaimedAsync(claimed, cancellationToken);
    }

    private async Task DeliverClaimedAsync(WebhookMessage message, CancellationToken cancellationToken)
    {
        var started = _clock.UtcNow;
        var endpoint = await _endpoints.GetAsync(message.OrganizationId, message.EndpointId, cancellationToken);
        WebhookHttpResult result;
        if (endpoint is null || !endpoint.IsDeliverable)
        {
            result = new WebhookHttpResult(null, null, "endpoint_disabled", 0, false);
            await PersistAttemptAsync(message, endpoint, started, result, abortEndpoint: false, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(message.DestinationUrl) || message.DestinationUrl != endpoint.Url)
        {
            result = new WebhookHttpResult(null, null, "destination_changed_requires_review", 0, false);
            await PersistAttemptAsync(message, endpoint, started, result, abortEndpoint: false, cancellationToken);
            return;
        }
        try
        {
            await _urls.ValidateForDeliveryAsync(message.DestinationUrl, cancellationToken);
            var secrets = await LoadPlaintextSecretsAsync(message.OrganizationId, endpoint.Id, cancellationToken);
            var timestamp = started.ToUnixTimeSeconds();
            var signature = _signer.Sign(message.Id, timestamp, message.PayloadJson, secrets);
            var headers = new Dictionary<string, string>
            {
                ["webhook-id"] = message.Id,
                ["webhook-timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
                ["webhook-signature"] = signature
            };
            result = await _http.PostAsync(message.DestinationUrl, message.PayloadJson, headers, cancellationToken);
        }
        catch (RequestValidationException ex)
        {
            result = new WebhookHttpResult(null, null, ex.Message, 0, false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            result = new WebhookHttpResult(null, null, "delivery_failed", 0, false);
        }

        await PersistAttemptAsync(message, endpoint, started, result, abortEndpoint: result.StatusCode == 410, cancellationToken);
    }

    private async Task PersistAttemptAsync(
        WebhookMessage message,
        WebhookEndpoint? endpoint,
        DateTimeOffset started,
        WebhookHttpResult result,
        bool abortEndpoint,
        CancellationToken cancellationToken)
    {
        await _uow.ExecuteInTransactionAsync(async () =>
        {
            var finished = _clock.UtcNow;
            var success = result.StatusCode is >= 200 and <= 299;
            message.LastAttemptAt = finished;
            message.AttemptCount += 1;
            message.LeaseUntil = null;

            await _queue.AddAttemptAsync(new WebhookDeliveryAttempt
            {
                Id = IdGenerator.New("att"),
                OrganizationId = message.OrganizationId,
                MessageId = message.Id,
                EndpointId = message.EndpointId,
                AttemptNumber = message.AttemptCount,
                StartedAt = started,
                FinishedAt = finished,
                HttpStatus = result.StatusCode,
                ResponseBody = Truncate(result.ResponseBody),
                Error = result.Error,
                DurationMs = result.DurationMs,
                Success = success
            }, cancellationToken);

            if (success)
            {
                message.Status = WebhookMessageStatuses.Delivered;
                if (endpoint is not null)
                {
                    ResetFailures(endpoint, finished);
                }
            }
            else if (abortEndpoint || result.StatusCode == 410)
            {
                message.Status = WebhookMessageStatuses.Failed;
                if (endpoint is not null)
                {
                    await DisableEndpointAsync(endpoint, finished, "gone", cancellationToken);
                }
            }
            else if (WebhookRetrySchedule.IsExhausted(message.AttemptCount))
            {
                message.Status = WebhookMessageStatuses.Failed;
                if (endpoint is not null && message.EventType != WebhookEventTypes.Ping)
                {
                    RecordFailure(endpoint, finished);
                    if (ShouldAutoDisable(endpoint, finished))
                    {
                        await DisableEndpointAsync(endpoint, finished, "consecutive_failures", cancellationToken);
                    }
                }
            }
            else
            {
                var retryAfter = ParseRetryAfter(result);
                message.Status = WebhookMessageStatuses.Retrying;
                message.NextAttemptAt = finished + WebhookRetrySchedule.DelayAfterFailure(message.AttemptCount, retryAfter);
                if (endpoint is not null && message.EventType != WebhookEventTypes.Ping)
                {
                    RecordFailure(endpoint, finished);
                    if (ShouldAutoDisable(endpoint, finished))
                    {
                        await DisableEndpointAsync(endpoint, finished, "consecutive_failures", cancellationToken);
                    }
                }
            }

            return 0;
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> LoadPlaintextSecretsAsync(string organizationId, string endpointId, CancellationToken cancellationToken)
    {
        var secrets = await _endpoints.ListActiveSecretsAsync(organizationId, endpointId, cancellationToken);
        if (secrets.Count == 0)
        {
            throw new RequestValidationException("Webhook endpoint is missing a signing secret.");
        }

        return secrets
            .OrderByDescending(x => x.Version)
            .Select(x => _protector.Unprotect(x.Ciphertext))
            .ToArray();
    }

    private static void ResetFailures(WebhookEndpoint endpoint, DateTimeOffset now)
    {
        endpoint.ConsecutiveFailures = 0;
        endpoint.FailureStartedAt = null;
        if (endpoint.Status == WebhookEndpointStatuses.Failing)
        {
            endpoint.Status = WebhookEndpointStatuses.Enabled;
            endpoint.UpdatedAt = now;
        }
    }

    private static void RecordFailure(WebhookEndpoint endpoint, DateTimeOffset now)
    {
        endpoint.ConsecutiveFailures += 1;
        endpoint.FailureStartedAt ??= now;
        endpoint.UpdatedAt = now;
        if (endpoint.Status == WebhookEndpointStatuses.Enabled && endpoint.ConsecutiveFailures >= ConsecutiveFailureMarkFailing)
        {
            endpoint.Status = WebhookEndpointStatuses.Failing;
        }
    }

    private static bool ShouldAutoDisable(WebhookEndpoint endpoint, DateTimeOffset now) =>
        endpoint.ConsecutiveFailures >= ConsecutiveFailureDisableThreshold
        || (endpoint.FailureStartedAt is { } started && now - started >= AutoDisableAfter && endpoint.ConsecutiveFailures >= ConsecutiveFailureMarkFailing);

    private async Task DisableEndpointAsync(WebhookEndpoint endpoint, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        if (endpoint.Status == WebhookEndpointStatuses.Disabled)
        {
            return;
        }

        endpoint.Status = WebhookEndpointStatuses.Disabled;
        endpoint.DisabledAt = now;
        endpoint.UpdatedAt = now;
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            endpoint_id = endpoint.Id,
            reason
        });
        await _outbox.EnqueueAsync(endpoint.OrganizationId, WebhookEventTypes.EndpointDisabled, payload, cancellationToken);
    }

    private static TimeSpan? ParseRetryAfter(WebhookHttpResult result)
    {
        if (result.StatusCode != 429)
        {
            return null;
        }

        return TimeSpan.FromSeconds(30);
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 2048 ? value : value[..2048];
    }
}

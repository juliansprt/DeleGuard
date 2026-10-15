using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Notifications;

namespace Deleguard.Application.Notifications;

public sealed record CreateWebhookCommand(
    string OrganizationId,
    string ApplicationId,
    string Description,
    string Url,
    IReadOnlyList<string> EventTypes);

public sealed record UpdateWebhookCommand(
    string OrganizationId,
    string EndpointId,
    string? Description,
    string? Url,
    IReadOnlyList<string>? EventTypes,
    string? Status);

public sealed class WebhookAdminHandler
{
    private readonly IWebhookEndpointStore _endpoints;
    private readonly IWebhookDeliveryQueue _queue;
    private readonly IWebhookSecretProtector _protector;
    private readonly IWebhookUrlValidator _urls;
    private readonly IApplicationDirectory _applications;
    private readonly DeliverWebhooksHandler _delivery;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public WebhookAdminHandler(
        IWebhookEndpointStore endpoints,
        IWebhookDeliveryQueue queue,
        IWebhookSecretProtector protector,
        IWebhookUrlValidator urls,
        IApplicationDirectory applications,
        DeliverWebhooksHandler delivery,
        IUnitOfWork uow,
        IClock clock)
    {
        _endpoints = endpoints;
        _queue = queue;
        _protector = protector;
        _urls = urls;
        _applications = applications;
        _delivery = delivery;
        _uow = uow;
        _clock = clock;
    }

    public async Task<IReadOnlyList<WebhookEndpointListItem>> ListAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken)
    {
        var items = await _endpoints.ListAsync(organizationId, applicationId, cancellationToken);
        var result = new List<WebhookEndpointListItem>(items.Count);
        foreach (var item in items)
        {
            var prefix = await _endpoints.LatestSecretPrefixAsync(organizationId, item.Id, cancellationToken);
            result.Add(ToListItem(item, prefix));
        }

        return result;
    }

    public async Task<WebhookEndpointListItem> GetAsync(string organizationId, string endpointId, CancellationToken cancellationToken)
    {
        var endpoint = await RequireEndpoint(organizationId, endpointId, cancellationToken);
        var prefix = await _endpoints.LatestSecretPrefixAsync(organizationId, endpoint.Id, cancellationToken);
        return ToListItem(endpoint, prefix);
    }

    public async Task<CreatedWebhookSecret> CreateAsync(CreateWebhookCommand command, CancellationToken cancellationToken)
    {
        ValidateDescription(command.Description);
        _urls.ValidateForConfiguration(command.Url.Trim());
        var application = await _applications.GetAsync(command.OrganizationId, command.ApplicationId, cancellationToken)
                          ?? throw new NotFoundException("Application not found.");
        if (!string.Equals(application.Kind, ApplicationKinds.Product, StringComparison.Ordinal))
        {
            throw new RequestValidationException("Webhooks belong to a product application.");
        }

        var types = NormalizeEventTypes(command.EventTypes);
        var now = _clock.UtcNow;
        var endpoint = new WebhookEndpoint
        {
            Id = IdGenerator.New("whk"),
            OrganizationId = command.OrganizationId,
            ApplicationId = command.ApplicationId,
            Description = command.Description.Trim(),
            Url = command.Url.Trim(),
            EventTypesJson = WebhookEventFilter.Serialize(types),
            Status = WebhookEndpointStatuses.Enabled,
            CreatedAt = now,
            UpdatedAt = now
        };
        var plaintext = _protector.CreatePlaintextSecret();
        await _endpoints.AddAsync(endpoint, cancellationToken);
        await _endpoints.AddSecretAsync(new WebhookEndpointSecret
        {
            Id = IdGenerator.New("whs"),
            OrganizationId = command.OrganizationId,
            EndpointId = endpoint.Id,
            Ciphertext = _protector.Protect(plaintext),
            Prefix = PrefixOf(plaintext),
            Version = 1,
            CreatedAt = now
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return new CreatedWebhookSecret(endpoint.Id, plaintext, PrefixOf(plaintext));
    }

    public async Task<WebhookEndpointListItem> UpdateAsync(UpdateWebhookCommand command, CancellationToken cancellationToken)
    {
        var endpoint = await RequireEndpoint(command.OrganizationId, command.EndpointId, cancellationToken);
        if (command.Description is not null)
        {
            ValidateDescription(command.Description);
            endpoint.Description = command.Description.Trim();
        }

        if (command.Url is not null)
        {
            _urls.ValidateForConfiguration(command.Url.Trim());
            endpoint.Url = command.Url.Trim();
        }

        if (command.EventTypes is not null)
        {
            endpoint.EventTypesJson = WebhookEventFilter.Serialize(NormalizeEventTypes(command.EventTypes));
        }

        if (command.Status is not null)
        {
            ApplyStatus(endpoint, command.Status, _clock.UtcNow);
        }

        endpoint.UpdatedAt = _clock.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);
        var prefix = await _endpoints.LatestSecretPrefixAsync(command.OrganizationId, endpoint.Id, cancellationToken);
        return ToListItem(endpoint, prefix);
    }

    public async Task<CreatedWebhookSecret> RotateSecretAsync(string organizationId, string endpointId, CancellationToken cancellationToken)
    {
        var endpoint = await RequireEndpoint(organizationId, endpointId, cancellationToken);
        var now = _clock.UtcNow;
        var plaintext = _protector.CreatePlaintextSecret();
        var current = await _endpoints.ListActiveSecretsAsync(organizationId, endpointId, cancellationToken);
        var nextVersion = current.Count == 0 ? 1 : current.Max(x => x.Version) + 1;
        await _endpoints.AddSecretAsync(new WebhookEndpointSecret
        {
            Id = IdGenerator.New("whs"),
            OrganizationId = organizationId,
            EndpointId = endpoint.Id,
            Ciphertext = _protector.Protect(plaintext),
            Prefix = PrefixOf(plaintext),
            Version = nextVersion,
            CreatedAt = now
        }, cancellationToken);
        await _endpoints.RevokeSecretsOlderThanAsync(organizationId, endpointId, 2, now, cancellationToken);
        endpoint.UpdatedAt = now;
        await _uow.SaveChangesAsync(cancellationToken);
        return new CreatedWebhookSecret(endpoint.Id, plaintext, PrefixOf(plaintext));
    }

    public async Task PingAsync(string organizationId, string endpointId, CancellationToken cancellationToken)
    {
        var endpoint = await RequireEndpoint(organizationId, endpointId, cancellationToken);
        if (!endpoint.IsDeliverable)
        {
            throw new ConflictException("Webhook endpoint is disabled.");
        }

        _urls.ValidateForConfiguration(endpoint.Url);
        var now = _clock.UtcNow;
        var messageId = IdGenerator.New("msg");
        var data = System.Text.Json.JsonSerializer.SerializeToElement(new { endpoint_id = endpoint.Id });
        await _queue.TryAddMessageAsync(new WebhookMessage
        {
            Id = messageId,
            OrganizationId = organizationId,
            EndpointId = endpoint.Id,
                        DestinationUrl = endpoint.Url,
            OutboxEventId = $"ping_{messageId}",
            EventType = WebhookEventTypes.Ping,
            PayloadJson = WebhookEnvelope.Create(messageId, WebhookEventTypes.Ping, now, data),
            Status = WebhookMessageStatuses.Pending,
            AttemptCount = 0,
            NextAttemptAt = now,
            CreatedAt = now
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        await _delivery.DeliverNowAsync(organizationId, messageId, cancellationToken);
    }

    public Task<IReadOnlyList<WebhookAttemptItem>> ListAttemptsAsync(string organizationId, string endpointId, CancellationToken cancellationToken) =>
        _queue.ListAttemptsAsync(organizationId, endpointId, 100, cancellationToken);

    public async Task ResendAsync(string organizationId, string endpointId, string messageId, CancellationToken cancellationToken)
    {
        var message = await _queue.GetMessageAsync(organizationId, endpointId, messageId, cancellationToken)
                      ?? throw new NotFoundException("Webhook message not found.");
        var now = _clock.UtcNow;
        message.Status = WebhookMessageStatuses.Pending;
        message.NextAttemptAt = now;
        message.LeaseUntil = null;
        if (WebhookRetrySchedule.IsExhausted(message.AttemptCount))
        {
            message.AttemptCount = WebhookRetrySchedule.MaxAttempts - 1;
        }

        await _uow.SaveChangesAsync(cancellationToken);
        await _delivery.DeliverNowAsync(organizationId, messageId, cancellationToken);
    }

    public async Task<int> RecoverAsync(string organizationId, string endpointId, DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken)
    {
        await RequireEndpoint(organizationId, endpointId, cancellationToken);
        ValidateRecoveryRange(since, until, _clock.UtcNow);
        var count = await _queue.RequeueFailedAsync(organizationId, endpointId, since, until, _clock.UtcNow, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return count;
    }

    public static DateTimeOffset ParseRecoveryInstant(string? raw, string name)
    {
        if (raw is null || !System.Text.RegularExpressions.Regex.IsMatch(raw, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$") ||
            !DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
            throw new RequestValidationException($"{name} must be a complete ISO-8601 instant including date and UTC offset.");
        return parsed;
    }

    public static void ValidateRecoveryRange(DateTimeOffset since, DateTimeOffset until, DateTimeOffset? now = null)
    {
        if (since > until || until > (now ?? DateTimeOffset.UtcNow))
            throw new RequestValidationException("Recovery requires since <= until <= now. Preserve both instants while awaiting approval.");
    }

    private async Task<WebhookEndpoint> RequireEndpoint(string organizationId, string endpointId, CancellationToken cancellationToken) =>
        await _endpoints.GetAsync(organizationId, endpointId, cancellationToken)
        ?? throw new NotFoundException("Webhook endpoint not found.");

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 200)
        {
            throw new RequestValidationException("description is required and must be 200 characters or fewer.");
        }
    }

    private static IReadOnlyList<string> NormalizeEventTypes(IReadOnlyList<string> eventTypes)
    {
        var invalid = eventTypes.Where(x => !string.IsNullOrWhiteSpace(x) && !WebhookEventTypes.IsSubscribable(x)).ToArray();
        if (invalid.Length > 0)
        {
            throw new RequestValidationException($"Unknown event type: {invalid[0]}");
        }

        return eventTypes.Where(WebhookEventTypes.IsSubscribable).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void ApplyStatus(WebhookEndpoint endpoint, string status, DateTimeOffset now)
    {
        var normalized = status.Trim().ToLowerInvariant();
        if (normalized is not (WebhookEndpointStatuses.Enabled or WebhookEndpointStatuses.Disabled))
        {
            throw new RequestValidationException("status must be enabled or disabled.");
        }

        if (normalized == WebhookEndpointStatuses.Enabled)
        {
            endpoint.Status = WebhookEndpointStatuses.Enabled;
            endpoint.DisabledAt = null;
            endpoint.ConsecutiveFailures = 0;
            endpoint.FailureStartedAt = null;
        }
        else
        {
            endpoint.Status = WebhookEndpointStatuses.Disabled;
            endpoint.DisabledAt = now;
        }
    }

    private static WebhookEndpointListItem ToListItem(WebhookEndpoint endpoint, string? prefix) =>
        new(
            endpoint.Id,
            endpoint.Description,
            endpoint.Url,
            endpoint.Status,
            WebhookEventFilter.Parse(endpoint.EventTypesJson),
            endpoint.ConsecutiveFailures,
            prefix,
            endpoint.CreatedAt,
            endpoint.UpdatedAt);

    private static string PrefixOf(string secret) =>
        secret.Length <= 12 ? secret : secret[..12];
}

using Deleguard.Domain.Notifications;

namespace Deleguard.Application.Abstractions;

public sealed record WebhookEndpointListItem(
    string Id,
    string Description,
    string Url,
    string Status,
    IReadOnlyList<string> EventTypes,
    int ConsecutiveFailures,
    string? SecretPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatedWebhookSecret(string EndpointId, string Secret, string SecretPrefix);

public sealed record WebhookAttemptItem(
    string Id,
    string MessageId,
    string EventType,
    int AttemptNumber,
    int? HttpStatus,
    string? Error,
    string? ResponseBody,
    int DurationMs,
    bool Success,
    DateTimeOffset FinishedAt);

public sealed record WebhookHttpResult(
    int? StatusCode,
    string? ResponseBody,
    string? Error,
    int DurationMs,
    bool TimedOut);

public interface IWebhookEndpointStore
{
    Task<IReadOnlyList<WebhookEndpoint>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WebhookEndpoint>> ListDeliverableMatchingAsync(string organizationId, string eventType, string? applicationId, CancellationToken cancellationToken);
    Task<WebhookEndpoint?> GetAsync(string organizationId, string endpointId, CancellationToken cancellationToken);
    Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken);
    Task<IReadOnlyList<WebhookEndpointSecret>> ListActiveSecretsAsync(string organizationId, string endpointId, CancellationToken cancellationToken);
    Task<string?> LatestSecretPrefixAsync(string organizationId, string endpointId, CancellationToken cancellationToken);
    Task AddSecretAsync(WebhookEndpointSecret secret, CancellationToken cancellationToken);
    Task RevokeSecretsOlderThanAsync(string organizationId, string endpointId, int keepLatest, DateTimeOffset revokedAt, CancellationToken cancellationToken);
}

public interface IWebhookDeliveryQueue
{
    Task<bool> TryAddMessageAsync(WebhookMessage message, CancellationToken cancellationToken);
    Task<IReadOnlyList<WebhookMessage>> ClaimDueAsync(int take, DateTimeOffset now, CancellationToken cancellationToken);
    Task<WebhookMessage?> ClaimAsync(string organizationId, string messageId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<WebhookMessage?> GetMessageAsync(string organizationId, string endpointId, string messageId, CancellationToken cancellationToken);
    Task AddAttemptAsync(WebhookDeliveryAttempt attempt, CancellationToken cancellationToken);
    Task<IReadOnlyList<WebhookAttemptItem>> ListAttemptsAsync(string organizationId, string endpointId, int take, CancellationToken cancellationToken);
    Task<int> RequeueFailedAsync(string organizationId, string endpointId, DateTimeOffset since, DateTimeOffset until, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IWebhookSigner
{
    string Sign(string messageId, long unixTimestampSeconds, string payload, IReadOnlyList<string> plaintextSecrets);
}

public interface IWebhookSecretProtector
{
    string CreatePlaintextSecret();
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

public interface IWebhookUrlValidator
{
    void ValidateForConfiguration(string url);
    Task ValidateForDeliveryAsync(string url, CancellationToken cancellationToken);
}

public interface IWebhookHttpClient
{
    Task<WebhookHttpResult> PostAsync(string url, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
}

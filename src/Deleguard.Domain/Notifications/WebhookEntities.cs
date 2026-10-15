namespace Deleguard.Domain.Notifications;

public static class WebhookEndpointStatuses
{
    public const string Enabled = "enabled";
    public const string Failing = "failing";
    public const string Disabled = "disabled";
}

public static class WebhookMessageStatuses
{
    public const string Pending = "pending";
    public const string Retrying = "retrying";
    public const string Delivering = "delivering";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
}

public static class WebhookEventTypes
{
    public const string AuthorizationDecided = "authorization.decided";
    public const string ApprovalResolved = "approval.resolved";
    public const string ApprovalExpired = "approval.expired";
    public const string ExecutionReported = "execution.reported";
    public const string EndpointDisabled = "webhook.endpoint.disabled";
    public const string Ping = "webhook.ping";

    public static readonly IReadOnlyList<string> Subscribable =
    [
        AuthorizationDecided,
        ApprovalResolved,
        ApprovalExpired,
        ExecutionReported,
        EndpointDisabled
    ];

    public static bool IsSubscribable(string eventType) =>
        Subscribable.Contains(eventType, StringComparer.Ordinal);
}

public sealed class WebhookEndpoint
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string Description { get; set; }
    public required string Url { get; set; }
    public required string EventTypesJson { get; set; }
    public required string Status { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? FailureStartedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeliverable =>
        Status is WebhookEndpointStatuses.Enabled or WebhookEndpointStatuses.Failing;
}

public sealed class WebhookEndpointSecret
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string EndpointId { get; init; }
    public required string Ciphertext { get; init; }
    public required string Prefix { get; init; }
    public int Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class WebhookMessage
{
    public string? DestinationUrl { get; init; }
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string EndpointId { get; init; }
    public required string OutboxEventId { get; init; }
    public required string EventType { get; init; }
    public required string PayloadJson { get; init; }
    public required string Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class WebhookDeliveryAttempt
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string MessageId { get; init; }
    public required string EndpointId { get; init; }
    public int AttemptNumber { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public int? HttpStatus { get; init; }
    public string? ResponseBody { get; init; }
    public string? Error { get; init; }
    public int DurationMs { get; init; }
    public bool Success { get; init; }
}

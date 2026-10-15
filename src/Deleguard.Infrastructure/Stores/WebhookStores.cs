using Deleguard.Application.Abstractions;
using Deleguard.Domain.Notifications;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Deleguard.Infrastructure.Stores;

public sealed class WebhookEndpointStore : IWebhookEndpointStore
{
    private readonly DeleguardDbContext _db;

    public WebhookEndpointStore(DeleguardDbContext db) => _db = db;

    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken)
    {
        var query = _db.WebhookEndpoints.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            query = query.Where(x => x.ApplicationId == applicationId);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListDeliverableMatchingAsync(
        string organizationId,
        string eventType,
        string? applicationId,
        CancellationToken cancellationToken)
    {
        var query = _db.WebhookEndpoints
            .Where(x => x.OrganizationId == organizationId
                        && (x.Status == WebhookEndpointStatuses.Enabled || x.Status == WebhookEndpointStatuses.Failing));
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            query = query.Where(x => x.ApplicationId == applicationId);
        }

        var endpoints = await query.ToListAsync(cancellationToken);
        return endpoints.Where(x => WebhookEventFilter.Accepts(x.EventTypesJson, eventType)).ToList();
    }

    public Task<WebhookEndpoint?> GetAsync(string organizationId, string endpointId, CancellationToken cancellationToken) =>
        _db.WebhookEndpoints.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == endpointId, cancellationToken);

    public async Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken) =>
        await _db.WebhookEndpoints.AddAsync(endpoint, cancellationToken);

    public async Task<IReadOnlyList<WebhookEndpointSecret>> ListActiveSecretsAsync(string organizationId, string endpointId, CancellationToken cancellationToken) =>
        await _db.WebhookEndpointSecrets
            .Where(x => x.OrganizationId == organizationId && x.EndpointId == endpointId && x.RevokedAt == null)
            .OrderByDescending(x => x.Version)
            .ToListAsync(cancellationToken);

    public async Task<string?> LatestSecretPrefixAsync(string organizationId, string endpointId, CancellationToken cancellationToken) =>
        await _db.WebhookEndpointSecrets.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EndpointId == endpointId && x.RevokedAt == null)
            .OrderByDescending(x => x.Version)
            .Select(x => x.Prefix)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddSecretAsync(WebhookEndpointSecret secret, CancellationToken cancellationToken) =>
        await _db.WebhookEndpointSecrets.AddAsync(secret, cancellationToken);

    public async Task RevokeSecretsOlderThanAsync(string organizationId, string endpointId, int keepLatest, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        var secrets = await _db.WebhookEndpointSecrets
            .Where(x => x.OrganizationId == organizationId && x.EndpointId == endpointId && x.RevokedAt == null)
            .OrderByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
        foreach (var secret in secrets.Skip(keepLatest))
        {
            secret.RevokedAt = revokedAt;
        }
    }
}

public sealed class WebhookDeliveryQueue : IWebhookDeliveryQueue
{
    private readonly DeleguardDbContext _db;

    public WebhookDeliveryQueue(DeleguardDbContext db) => _db = db;

    public async Task<bool> TryAddMessageAsync(WebhookMessage message, CancellationToken cancellationToken)
    {
        var exists = await _db.WebhookMessages.AnyAsync(
            x => x.OutboxEventId == message.OutboxEventId && x.EndpointId == message.EndpointId,
            cancellationToken);
        if (exists || _db.WebhookMessages.Local.Any(x => x.OutboxEventId == message.OutboxEventId && x.EndpointId == message.EndpointId))
        {
            return false;
        }

        try
        {
            await _db.WebhookMessages.AddAsync(message, cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<WebhookMessage>> ClaimDueAsync(int take, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await _db.WebhookMessages
            .FromSql($"""
                SELECT * FROM webhook_messages
                WHERE (
                    ("Status" IN ('pending', 'retrying') AND "NextAttemptAt" <= {now})
                    OR ("Status" = 'delivering' AND "LeaseUntil" IS NOT NULL AND "LeaseUntil" < {now})
                )
                AND NOT EXISTS (
                    SELECT 1 FROM webhook_messages AS d
                    WHERE d."EndpointId" = webhook_messages."EndpointId"
                      AND d."Status" = 'delivering'
                      AND d."LeaseUntil" IS NOT NULL
                      AND d."LeaseUntil" > {now}
                      AND d."Id" <> webhook_messages."Id"
                )
                ORDER BY "NextAttemptAt"
                LIMIT {take}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var lease = now + WebhookRetrySchedule.LeaseDuration;
        foreach (var row in rows)
        {
            row.Status = WebhookMessageStatuses.Delivering;
            row.LeaseUntil = lease;
        }

        return rows;
    }

    public async Task<WebhookMessage?> ClaimAsync(string organizationId, string messageId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var message = await _db.WebhookMessages.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Id == messageId,
            cancellationToken);
        if (message is null)
        {
            return null;
        }

        if (message.Status == WebhookMessageStatuses.Delivering && message.LeaseUntil is { } lease && lease > now)
        {
            return null;
        }

        message.Status = WebhookMessageStatuses.Delivering;
        message.LeaseUntil = now + WebhookRetrySchedule.LeaseDuration;
        return message;
    }

    public Task<WebhookMessage?> GetMessageAsync(string organizationId, string endpointId, string messageId, CancellationToken cancellationToken) =>
        _db.WebhookMessages.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.EndpointId == endpointId && x.Id == messageId,
            cancellationToken);

    public async Task AddAttemptAsync(WebhookDeliveryAttempt attempt, CancellationToken cancellationToken) =>
        await _db.WebhookDeliveryAttempts.AddAsync(attempt, cancellationToken);

    public async Task<IReadOnlyList<WebhookAttemptItem>> ListAttemptsAsync(string organizationId, string endpointId, int take, CancellationToken cancellationToken) =>
        await (
            from attempt in _db.WebhookDeliveryAttempts.AsNoTracking()
            join message in _db.WebhookMessages.AsNoTracking() on attempt.MessageId equals message.Id
            where attempt.OrganizationId == organizationId && attempt.EndpointId == endpointId
            orderby attempt.FinishedAt descending
            select new WebhookAttemptItem(
                attempt.Id,
                attempt.MessageId,
                message.EventType,
                attempt.AttemptNumber,
                attempt.HttpStatus,
                attempt.Error,
                attempt.ResponseBody,
                attempt.DurationMs,
                attempt.Success,
                attempt.FinishedAt)
        ).Take(take).ToListAsync(cancellationToken);

    public async Task<int> RequeueFailedAsync(string organizationId, string endpointId, DateTimeOffset since, DateTimeOffset until, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var items = await _db.WebhookMessages
            .Where(x => x.OrganizationId == organizationId
                        && x.EndpointId == endpointId
                        && x.Status == WebhookMessageStatuses.Failed
                        && x.CreatedAt >= since && x.CreatedAt <= until)
            .ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            item.Status = WebhookMessageStatuses.Pending;
            item.NextAttemptAt = now;
            item.LeaseUntil = null;
            if (WebhookRetrySchedule.IsExhausted(item.AttemptCount))
            {
                item.AttemptCount = WebhookRetrySchedule.MaxAttempts - 1;
            }
        }

        return items.Count;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

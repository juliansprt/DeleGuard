using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Notifications;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Notifications;

namespace Deleguard.UnitTests;

public sealed class WebhookHandlerTests
{
    [Fact]
    public async Task Fanout_creates_one_message_per_matching_endpoint()
    {
        var harness = new Harness();
        var endpoint = harness.AddEndpoint("https://example.com/a", [WebhookEventTypes.AuthorizationDecided]);
        harness.AddEndpoint("https://example.com/b", [WebhookEventTypes.ApprovalResolved]);
        harness.EnqueueOutbox("authorization.decided", """{"decision":"ALLOW"}""");

        var count = await harness.Fanout.HandleAsync(50, CancellationToken.None);
        Assert.Equal(1, count);
        var message = Assert.Single(harness.Messages);
        Assert.Equal(endpoint.Id, message.EndpointId);
        Assert.Equal("authorization.decided", message.EventType);
        Assert.Contains("\"decision\":\"ALLOW\"", message.PayloadJson);
        Assert.NotNull(harness.OutboxItems[0].ProcessedAt);
    }

    [Fact]
    public async Task Delivery_success_marks_delivered()
    {
        var harness = new Harness { HttpStatus = 200 };
        var endpoint = harness.AddEndpoint("https://example.com/ok", []);
        harness.EnqueueOutbox("authorization.decided", """{"decision":"DENY"}""");
        await harness.Fanout.HandleAsync(50, CancellationToken.None);

        var delivered = await harness.Delivery.HandleDueAsync(10, CancellationToken.None);
        Assert.Equal(1, delivered);
        Assert.Equal(WebhookMessageStatuses.Delivered, harness.Messages[0].Status);
        Assert.Equal(0, endpoint.ConsecutiveFailures);
        Assert.True(harness.Posts.Count == 1);
        Assert.Equal(harness.Messages[0].Id, harness.Posts[0].Headers["webhook-id"]);
    }

    [Fact]
    public async Task Delivery_500_schedules_retry()
    {
        var harness = new Harness { HttpStatus = 500 };
        harness.AddEndpoint("https://example.com/fail", []);
        harness.EnqueueOutbox("authorization.decided", "{}");
        await harness.Fanout.HandleAsync(50, CancellationToken.None);
        await harness.Delivery.HandleDueAsync(10, CancellationToken.None);

        Assert.Equal(WebhookMessageStatuses.Retrying, harness.Messages[0].Status);
        Assert.True(harness.Messages[0].NextAttemptAt > harness.UtcNow);
        Assert.Equal(1, harness.Messages[0].AttemptCount);
    }

    [Fact]
    public async Task Delivery_410_disables_endpoint()
    {
        var harness = new Harness { HttpStatus = 410 };
        var endpoint = harness.AddEndpoint("https://example.com/gone", []);
        harness.EnqueueOutbox("authorization.decided", "{}");
        await harness.Fanout.HandleAsync(50, CancellationToken.None);
        await harness.Delivery.HandleDueAsync(10, CancellationToken.None);

        Assert.Equal(WebhookEndpointStatuses.Disabled, endpoint.Status);
        Assert.Equal(WebhookMessageStatuses.Failed, harness.Messages[0].Status);
        Assert.Contains(harness.OutboxItems, x => x.EventType == WebhookEventTypes.EndpointDisabled);
    }

    [Fact]
    public async Task Queued_payload_never_follows_a_changed_endpoint_destination()
    {
        var harness = new Harness();
        var endpoint = harness.AddEndpoint("https://example.com/reviewed", []);
        harness.EnqueueOutbox("authorization.decided", "{}");
        await harness.Fanout.HandleAsync(10, default);
        endpoint.Url = "https://different.example.com/unreviewed";
        await harness.Delivery.HandleDueAsync(10, default);
        Assert.Empty(harness.Posts);
        Assert.Equal("destination_changed_requires_review", Assert.Single(harness.Attempts).Error);
    }

    private sealed class Harness : IClock, IUnitOfWork, IOutbox, IWebhookEndpointStore, IWebhookDeliveryQueue, IWebhookSigner, IWebhookSecretProtector, IWebhookUrlValidator, IWebhookHttpClient
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-13T12:00:00Z");
        public int HttpStatus { get; set; } = 200;
        public List<OutboxEvent> OutboxItems { get; } = [];
        public List<WebhookEndpoint> Endpoints { get; } = [];
        public List<WebhookEndpointSecret> Secrets { get; } = [];
        public List<WebhookMessage> Messages { get; } = [];
        public List<WebhookDeliveryAttempt> Attempts { get; } = [];
        public List<(string Url, string Body, IReadOnlyDictionary<string, string> Headers)> Posts { get; } = [];

        public FanoutOutboxHandler Fanout { get; }
        public DeliverWebhooksHandler Delivery { get; }

        public Harness()
        {
            Fanout = new FanoutOutboxHandler(this, this, this, this, this);
            Delivery = new DeliverWebhooksHandler(this, this, this, this, this, this, this, this, this);
        }

        public WebhookEndpoint AddEndpoint(string url, IReadOnlyList<string> types)
        {
            var endpoint = new WebhookEndpoint
            {
                Id = "whk_" + Endpoints.Count,
                OrganizationId = "org_demo",
                ApplicationId = "app_demo",
                Description = "t",
                Url = url,
                EventTypesJson = WebhookEventFilter.Serialize(types),
                Status = WebhookEndpointStatuses.Enabled,
                CreatedAt = UtcNow,
                UpdatedAt = UtcNow
            };
            Endpoints.Add(endpoint);
            Secrets.Add(new WebhookEndpointSecret
            {
                Id = "whs_" + Secrets.Count,
                OrganizationId = "org_demo",
                EndpointId = endpoint.Id,
                Ciphertext = StandardWebhookSignature.CreateSecret(),
                Prefix = "whsec_test",
                Version = 1,
                CreatedAt = UtcNow
            });
            return endpoint;
        }

        public void EnqueueOutbox(string type, string json) =>
            OutboxItems.Add(new OutboxEvent
            {
                Id = "obx_" + OutboxItems.Count,
                OrganizationId = "org_demo",
                EventType = type,
                PayloadJson = json,
                CreatedAt = UtcNow
            });

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) =>
            await action();

        public Task EnqueueAsync(string organizationId, string eventType, JsonElement payload, CancellationToken cancellationToken)
        {
            OutboxItems.Add(new OutboxEvent
            {
                Id = "obx_" + OutboxItems.Count,
                OrganizationId = organizationId,
                EventType = eventType,
                PayloadJson = payload.GetRawText(),
                CreatedAt = UtcNow
            });
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxEvent>> DequeueUnprocessedAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OutboxEvent>>(OutboxItems.Where(x => x.ProcessedAt is null).Take(take).ToList());

        public Task MarkProcessedAsync(string id, DateTimeOffset processedAt, CancellationToken cancellationToken)
        {
            var item = OutboxItems.First(x => x.Id == id);
            item.ProcessedAt = processedAt;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WebhookEndpoint>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WebhookEndpoint>>(Endpoints
                .Where(x => x.OrganizationId == organizationId && (applicationId is null || x.ApplicationId == applicationId))
                .ToList());

        public Task<IReadOnlyList<WebhookEndpoint>> ListDeliverableMatchingAsync(string organizationId, string eventType, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WebhookEndpoint>>(
                Endpoints.Where(x =>
                    x.OrganizationId == organizationId
                    && (applicationId is null || x.ApplicationId == applicationId)
                    && x.IsDeliverable
                    && WebhookEventFilter.Accepts(x.EventTypesJson, eventType)).ToList());

        public Task<WebhookEndpoint?> GetAsync(string organizationId, string endpointId, CancellationToken cancellationToken) =>
            Task.FromResult(Endpoints.FirstOrDefault(x => x.OrganizationId == organizationId && x.Id == endpointId));

        public Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken)
        {
            Endpoints.Add(endpoint);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WebhookEndpointSecret>> ListActiveSecretsAsync(string organizationId, string endpointId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WebhookEndpointSecret>>(Secrets.Where(x => x.EndpointId == endpointId && x.RevokedAt is null).ToList());

        public Task<string?> LatestSecretPrefixAsync(string organizationId, string endpointId, CancellationToken cancellationToken) =>
            Task.FromResult(Secrets.LastOrDefault(x => x.EndpointId == endpointId)?.Prefix);

        public Task AddSecretAsync(WebhookEndpointSecret secret, CancellationToken cancellationToken)
        {
            Secrets.Add(secret);
            return Task.CompletedTask;
        }

        public Task RevokeSecretsOlderThanAsync(string organizationId, string endpointId, int keepLatest, DateTimeOffset revokedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> TryAddMessageAsync(WebhookMessage message, CancellationToken cancellationToken)
        {
            if (Messages.Any(x => x.OutboxEventId == message.OutboxEventId && x.EndpointId == message.EndpointId))
            {
                return Task.FromResult(false);
            }

            Messages.Add(message);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<WebhookMessage>> ClaimDueAsync(int take, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var due = Messages.Where(x =>
                    (x.Status is WebhookMessageStatuses.Pending or WebhookMessageStatuses.Retrying && x.NextAttemptAt <= now)
                    || (x.Status == WebhookMessageStatuses.Delivering && x.LeaseUntil < now))
                .Take(take)
                .ToList();
            foreach (var message in due)
            {
                message.Status = WebhookMessageStatuses.Delivering;
                message.LeaseUntil = now + WebhookRetrySchedule.LeaseDuration;
            }

            return Task.FromResult<IReadOnlyList<WebhookMessage>>(due);
        }

        public Task<WebhookMessage?> ClaimAsync(string organizationId, string messageId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var message = Messages.FirstOrDefault(x => x.OrganizationId == organizationId && x.Id == messageId);
            if (message is null)
            {
                return Task.FromResult<WebhookMessage?>(null);
            }

            message.Status = WebhookMessageStatuses.Delivering;
            message.LeaseUntil = now + WebhookRetrySchedule.LeaseDuration;
            return Task.FromResult<WebhookMessage?>(message);
        }

        public Task<WebhookMessage?> GetMessageAsync(string organizationId, string endpointId, string messageId, CancellationToken cancellationToken) =>
            Task.FromResult(Messages.FirstOrDefault(x => x.Id == messageId && x.EndpointId == endpointId));

        public Task AddAttemptAsync(WebhookDeliveryAttempt attempt, CancellationToken cancellationToken)
        {
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WebhookAttemptItem>> ListAttemptsAsync(string organizationId, string endpointId, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WebhookAttemptItem>>([]);

        public Task<int> RequeueFailedAsync(string organizationId, string endpointId, DateTimeOffset since, DateTimeOffset until, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public string Sign(string messageId, long unixTimestampSeconds, string payload, IReadOnlyList<string> plaintextSecrets) =>
            StandardWebhookSignature.Sign(messageId, unixTimestampSeconds, payload, plaintextSecrets);

        public string CreatePlaintextSecret() => StandardWebhookSignature.CreateSecret();
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
        public void ValidateForConfiguration(string url) { }
        public Task ValidateForDeliveryAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WebhookHttpResult> PostAsync(string url, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            Posts.Add((url, jsonBody, headers));
            return Task.FromResult(new WebhookHttpResult(HttpStatus, "ok", null, 5, false));
        }
    }
}

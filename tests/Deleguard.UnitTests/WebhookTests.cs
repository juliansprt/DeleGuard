using System.Net;
using Deleguard.Application.Notifications;
using Deleguard.Domain.Notifications;

namespace Deleguard.UnitTests;

public sealed class WebhookTests
{
    [Fact]
    public void Standard_webhooks_signature_round_trips()
    {
        var secret = StandardWebhookSignature.CreateSecret();
        const string payload = """{"id":"msg_1","type":"webhook.ping","timestamp":"2026-01-01T00:00:00Z","data":{}}""";
        var signature = StandardWebhookSignature.Sign("msg_1", 1_704_067_200, payload, [secret]);
        Assert.StartsWith("v1,", signature);
        Assert.True(StandardWebhookSignature.Verify(
            secret,
            "msg_1",
            "1704067200",
            payload,
            signature,
            DateTimeOffset.FromUnixTimeSeconds(1_704_067_200),
            TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Dual_secrets_are_space_delimited()
    {
        var current = StandardWebhookSignature.CreateSecret();
        var previous = StandardWebhookSignature.CreateSecret();
        var header = StandardWebhookSignature.Sign("msg_2", 1_704_067_200, "{}", [current, previous]);
        Assert.Equal(2, header.Split(' ').Length);
        Assert.True(StandardWebhookSignature.Verify(previous, "msg_2", "1704067200", "{}", header, DateTimeOffset.FromUnixTimeSeconds(1_704_067_200), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Tampered_payload_fails_verification()
    {
        var secret = StandardWebhookSignature.CreateSecret();
        var signature = StandardWebhookSignature.Sign("msg_3", 1_704_067_200, "{}", [secret]);
        Assert.False(StandardWebhookSignature.Verify(secret, "msg_3", "1704067200", "{\"x\":1}", signature, DateTimeOffset.FromUnixTimeSeconds(1_704_067_200), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Retry_schedule_exhausts_after_eight_attempts()
    {
        Assert.False(WebhookRetrySchedule.IsExhausted(7));
        Assert.True(WebhookRetrySchedule.IsExhausted(8));
        var delay = WebhookRetrySchedule.DelayAfterFailure(1, random: new Random(1));
        Assert.InRange(delay.TotalSeconds, 4, 6);
    }

    [Fact]
    public void Event_filter_empty_accepts_all()
    {
        Assert.True(WebhookEventFilter.Accepts("[]", WebhookEventTypes.AuthorizationDecided));
        var json = WebhookEventFilter.Serialize([WebhookEventTypes.ApprovalResolved, "not-a-type"]);
        Assert.False(WebhookEventFilter.Accepts(json, WebhookEventTypes.AuthorizationDecided));
        Assert.True(WebhookEventFilter.Accepts(json, WebhookEventTypes.ApprovalResolved));
    }

    [Fact]
    public void Envelope_is_compact_json()
    {
        var json = WebhookEnvelope.Create("msg_x", "authorization.decided", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), System.Text.Json.JsonSerializer.SerializeToElement(new { decision_id = "dec_1" }));
        Assert.Contains("\"id\":\"msg_x\"", json);
        Assert.DoesNotContain("\n", json);
    }

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.0.0.5/hooks")]
    [InlineData("https://192.168.1.9/hooks")]
    [InlineData("https://[::1]/hooks")]
    public void Private_literal_urls_are_blocked(string url)
    {
        var inspection = WebhookUrlSafety.Inspect(url);
        Assert.True(inspection.LiteralAddress is not null && WebhookUrlSafety.IsBlockedAddress(inspection.LiteralAddress));
    }

    [Fact]
    public void Public_https_url_is_allowed_shape()
    {
        var inspection = WebhookUrlSafety.Inspect("https://hooks.example.com/deleguard");
        Assert.True(inspection.IsHttps);
        Assert.Null(inspection.LiteralAddress);
    }

    [Fact]
    public void Localhost_host_is_detected()
    {
        Assert.True(WebhookUrlSafety.IsLocalhostHost("localhost"));
        Assert.True(WebhookUrlSafety.IsLocalhostHost("metadata.google.internal"));
        Assert.False(WebhookUrlSafety.IsLocalhostHost("hooks.example.com"));
    }
}

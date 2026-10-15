using Deleguard.Application;
using Deleguard.Application.Notifications;

namespace Deleguard.UnitTests;

public sealed class RecoveryInstantTests
{
    [Theory]
    [InlineData("12:00")]
    [InlineData("2026-09-24")]
    [InlineData("2026-09-24T12:00:00")]
    [InlineData("2026-09-24T12:00:00Z trailing")]
    [InlineData("2026-09-31T12:00:00Z")]
    public void Recovery_never_infers_date_or_timezone(string raw) =>
        Assert.Throws<RequestValidationException>(() => WebhookAdminHandler.ParseRecoveryInstant(raw, "until"));

    [Theory]
    [InlineData("2026-09-24T12:00:00Z")]
    [InlineData("2026-09-24T07:00:00-05:00")]
    [InlineData("2026-09-24T12:00:00.0000000+00:00")]
    public void Complete_instants_bind_the_same_utc_cutoff(string raw) =>
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero), WebhookAdminHandler.ParseRecoveryInstant(raw, "until"));
}

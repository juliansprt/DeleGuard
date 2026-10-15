namespace Deleguard.Domain.Notifications;

public static class WebhookRetrySchedule
{
    public const int MaxAttempts = 8;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(5),
        TimeSpan.FromHours(10),
        TimeSpan.FromHours(10)
    ];

    public static bool IsExhausted(int attemptCount) => attemptCount >= MaxAttempts;

    public static TimeSpan DelayAfterFailure(int attemptCount, TimeSpan? retryAfter = null, Random? random = null)
    {
        if (retryAfter is { } header && header > TimeSpan.Zero && header <= TimeSpan.FromHours(24))
        {
            return header;
        }

        var index = Math.Clamp(attemptCount - 1, 0, Delays.Length - 1);
        var delay = Delays[index];
        var jitter = 1d + (((random ?? Random.Shared).NextDouble() * 0.2d) - 0.1d);
        return TimeSpan.FromMilliseconds(Math.Max(250, delay.TotalMilliseconds * jitter));
    }
}

namespace Deleguard.Domain.Common;

public static class DurationParser
{
    public static TimeSpan Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Duration is required.", nameof(value));
        }

        value = value.Trim();
        var unit = value[^1];
        if (!int.TryParse(value[..^1], out var amount) || amount < 0)
        {
            throw new FormatException($"Invalid duration '{value}'.");
        }

        return unit switch
        {
            's' or 'S' => TimeSpan.FromSeconds(amount),
            'm' or 'M' => TimeSpan.FromMinutes(amount),
            'h' or 'H' => TimeSpan.FromHours(amount),
            'd' or 'D' => TimeSpan.FromDays(amount),
            _ => throw new FormatException($"Unknown duration unit in '{value}'.")
        };
    }

    public static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value.Ticks % TimeSpan.TicksPerDay == 0 && value.TotalDays >= 1)
        {
            return $"{(int)value.TotalDays}d";
        }

        if (value.Ticks % TimeSpan.TicksPerHour == 0 && value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}h";
        }

        if (value.Ticks % TimeSpan.TicksPerMinute == 0 && value.TotalMinutes >= 1)
        {
            return $"{(int)value.TotalMinutes}m";
        }

        return $"{(int)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero)}s";
    }
}

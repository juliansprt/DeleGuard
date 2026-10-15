using System.Security.Cryptography;
using System.Text;

namespace Deleguard.Domain.Notifications;

public static class StandardWebhookSignature
{
    public const string SecretPrefix = "whsec_";
    public const string SignatureVersion = "v1";

    public static string CreateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return SecretPrefix + Convert.ToBase64String(bytes);
    }

    public static string Sign(string messageId, long unixTimestampSeconds, string payload, IReadOnlyList<string> secrets)
    {
        if (secrets.Count == 0)
        {
            throw new InvalidOperationException("At least one signing secret is required.");
        }

        var signed = $"{messageId}.{unixTimestampSeconds}.{payload}";
        var signatures = secrets.Select(secret => $"{SignatureVersion},{Convert.ToBase64String(Compute(secret, signed))}");
        return string.Join(' ', signatures);
    }

    public static bool Verify(string secret, string messageId, string timestampHeader, string payload, string signatureHeader, DateTimeOffset now, TimeSpan tolerance)
    {
        if (!long.TryParse(timestampHeader, out var unix) || unix <= 0)
        {
            return false;
        }

        var sent = DateTimeOffset.FromUnixTimeSeconds(unix);
        if ((now - sent).Duration() > tolerance)
        {
            return false;
        }

        var signed = $"{messageId}.{unix}.{payload}";
        var expected = Compute(secret, signed);
        foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var comma = part.IndexOf(',');
            if (comma < 0)
            {
                continue;
            }

            var version = part[..comma];
            var value = part[(comma + 1)..];
            if (!string.Equals(version, SignatureVersion, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var actual = Convert.FromBase64String(value);
                if (CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // Ignore malformed signatures and keep checking remaining items.
            }
        }

        return false;
    }

    public static byte[] DecodeSecret(string secret)
    {
        var raw = secret.StartsWith(SecretPrefix, StringComparison.Ordinal)
            ? secret[SecretPrefix.Length..]
            : secret;
        return Convert.FromBase64String(raw);
    }

    private static byte[] Compute(string secret, string signedContent) =>
        HMACSHA256.HashData(DecodeSecret(secret), Encoding.UTF8.GetBytes(signedContent));
}

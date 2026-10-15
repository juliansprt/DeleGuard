using System.Security.Cryptography;
using System.Text;

namespace Deleguard.McpGuard;

public static class GuardHttpAuth
{
    public const string MissingTokenMessage =
        "Deleguard__HttpToken is required in --http mode. Cursor must send Authorization: Bearer <token> and must not register downstream MCPs beside the Guard.";

    public static bool Accepts(string? authorizationHeader, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = authorizationHeader[prefix.Length..].Trim();
        var left = Encoding.UTF8.GetBytes(provided);
        var right = Encoding.UTF8.GetBytes(expectedToken);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}

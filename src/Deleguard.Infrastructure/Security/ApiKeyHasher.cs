using System.Security.Cryptography;
using System.Text;

namespace Deleguard.Infrastructure.Security;

public static class ApiKeyHasher
{
    public static string Hash(string apiKey, string pepper)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pepper + ":" + apiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Prefix(string apiKey) =>
        apiKey.Length <= 12 ? apiKey : apiKey[..12];
}

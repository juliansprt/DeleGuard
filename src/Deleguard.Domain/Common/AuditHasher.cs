namespace Deleguard.Domain.Common;

public static class AuditHasher
{
    public const string Genesis = "genesis";

    public static string Compute(string? previousHash, string canonicalPayload) =>
        CanonicalJson.HashHex($"{previousHash ?? Genesis}|{canonicalPayload}");
}

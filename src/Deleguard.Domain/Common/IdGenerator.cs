namespace Deleguard.Domain.Common;

public static class IdGenerator
{
    public static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}

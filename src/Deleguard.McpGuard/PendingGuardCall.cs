using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Deleguard.McpGuard;

public sealed record PendingGuardCall(GuardMapping Mapping, JsonElement Arguments, DateTimeOffset ExpiresAt, string? ConnectionHash = null);

public sealed class PendingGuardStore
{
    private readonly string _root;
    private readonly IDataProtector _protector;
    public PendingGuardStore(string root)
    {
        _root = root;
        var keys = Directory.CreateDirectory(Path.Combine(root, "keys"));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _protector = DataProtectionProvider.Create(keys, options =>
        {
            options.SetApplicationName("Deleguard.LocalMcp.Pending.v1");
            if (OperatingSystem.IsWindows()) options.ProtectKeysWithDpapi();
        }).CreateProtector("pending-arguments");
    }
    private string PathFor(string id) => Path.Combine(_root,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".pending");
    public void Save(string id, GuardMapping mapping, JsonElement arguments, string connectionHash)
    {
        if (arguments.GetRawText().Length > 65536) throw new InvalidOperationException("Tool arguments exceed the pending payload limit.");
        foreach (var path in Directory.EnumerateFiles(_root, "*.pending"))
            if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7)) File.Delete(path);
        var destination = PathFor(id);
        var temp = destination + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, _protector.Protect(JsonSerializer.Serialize(new PendingGuardCall(mapping, arguments, DateTimeOffset.UtcNow.AddDays(7), connectionHash))));
        File.Move(temp, destination, true);
    }
    public PendingGuardCall? Load(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;
        var call = JsonSerializer.Deserialize<PendingGuardCall>(_protector.Unprotect(File.ReadAllText(path)));
        if (call?.ExpiresAt > DateTimeOffset.UtcNow) return call;
        File.Delete(path);
        return null;
    }
    public void Remove(string id) => File.Delete(PathFor(id));
}

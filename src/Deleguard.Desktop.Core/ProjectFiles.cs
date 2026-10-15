using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Deleguard.Desktop.Core;

public sealed class SetupException(string code) : Exception(code) { public string Code { get; } = code; }

public static class ProjectFiles
{
    public static string SafePath(string root, string relative)
    {
        root = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new SetupException("path_outside_project");
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            // GetAttributes also detects dangling reparse points on supported platforms.
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new SetupException("linked_path"); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }

    public static void PrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User ?? throw new SetupException("credential_unavailable");
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(user);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void WritePrivate(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temp = Path.Combine(directory, ".write-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                PrivateFile(temp);
                var bytes = Encoding.UTF8.GetBytes(content);
                file.Write(bytes); file.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static ProjectConnection ReadConnection(string path)
    {
        var raw = File.ReadAllText(path);
        if (raw.Length > 65536) throw new SetupException("invalid_profile");
        using var doc = JsonDocument.Parse(raw);
        var json = doc.RootElement;
        var config = ParseConnection(json);
        if (!string.Equals(Path.GetFullPath(path), config.ConfigPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SetupException("project_moved");
        return config;
    }
    public static ProjectConnection ParseConnection(JsonElement json)
    {
        var config = new ProjectConnection(json.GetProperty("root").GetString()!, json.GetProperty("adapter").GetString()!,
            json.GetProperty("url").GetString()!, json.GetProperty("project").GetString()!, json.GetProperty("agent").GetString()!,
            json.GetProperty("environment").GetString()!, json.GetProperty("credential_id").GetString()!, json.GetProperty("server_scope").Clone());
        if (json.GetProperty("schema_version").GetInt32() != 2 || json.GetProperty("credential_storage").GetString() != "project-vault")
            throw new SetupException("migration_required");
        config.Validate();
        return config;
    }
}

public sealed record ProjectConnection(string Root, string Adapter, string Url, string Project, string Agent,
    string Environment, string CredentialId, JsonElement Scope)
{
    public string Base => ProjectFiles.SafePath(Root, ".deleguard/" + Adapter);
    public string ConfigPath => ProjectFiles.SafePath(Root, ".deleguard/" + Adapter + "/config.json");
    public string VaultPath => ProjectFiles.SafePath(Root, ".deleguard/" + Adapter + "/credential.vault");
    public string SessionPath => ProjectFiles.SafePath(Root, ".deleguard/" + Adapter + "/session.json");
    public void Validate()
    {
        if (Adapter is not ("cursor" or "vscode" or "claude") || !Directory.Exists(Root) || Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)) != Root)
            throw new SetupException("invalid_project");
        ValidateUrl(Url);
        if (new[] { Project, Agent, Environment }.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 120 || v.Any(char.IsControl)))
            throw new SetupException("invalid_profile");
        foreach (var key in new[] { "organization_id", "application_id", "agent_id" })
            if (!Scope.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new SetupException("invalid_profile");
        if (CredentialId != ComputeIdentity()) throw new SetupException("project_moved");
        _ = ConfigPath;
    }
    public static Uri ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/" || url.Any(char.IsWhiteSpace)
            || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.Host is "localhost" or "127.0.0.1" or "[::1]"))
            throw new SetupException("invalid_server");
        return uri;
    }
    public string ComputeIdentity()
    {
        var scope = "{" + string.Join(",", new[] { "agent_id", "application_id", "organization_id" }.Select(k => Quote(k) + ":" + Quote(Scope.GetProperty(k).GetString()!))) + "}";
        var entries = new SortedDictionary<string,string>(StringComparer.Ordinal)
        {
            ["adapter"] = Quote(Adapter), ["agent"] = Quote(Agent), ["environment"] = Quote(Environment), ["project"] = Quote(Project),
            ["root"] = Quote(OperatingSystem.IsWindows() ? Root.ToLowerInvariant() : Root), ["server_scope"] = scope, ["url"] = Quote(Url)
        };
        var canonical = "{" + string.Join(",", entries.Select(e => Quote(e.Key) + ":" + e.Value)) + "}";
        return "ide-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..40];
    }
    // Match Python's ensure_ascii JSON encoding, including surrogate pairs.
    private static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value) text.Append(c switch
        {
            '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
            < ' ' or > '~' => "\\u" + ((int)c).ToString("x4"), _ => c.ToString()
        });
        return text.Append('"').ToString();
    }
}

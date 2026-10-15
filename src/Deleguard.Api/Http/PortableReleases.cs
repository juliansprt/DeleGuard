using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deleguard.Api.Http;

public sealed record PortableRelease(string Platform,string Version,string File,string Sha256,bool Signed,
    [property:JsonPropertyName("platform_tested")]bool PlatformTested,string Channel);

/// <summary>Only operator-approved, platform-tested releases are exposed. Private builds stay local.</summary>
public static class PortableReleases
{
    public static readonly string[] Platforms=["win-x64","win-arm64","osx-arm64","osx-x64","linux-x64","linux-arm64"];
    public static PortableRelease? Find(HttpContext http,string platform)
    {
        if(!Platforms.Contains(platform))return null;
        var config=http.RequestServices.GetRequiredService<IConfiguration>();
        var directory=config["PortableInstaller:ReleaseDirectory"];
        if(string.IsNullOrWhiteSpace(directory)||!Path.IsPathFullyQualified(directory))return null;
        try
        {
            var path=Path.Combine(directory,platform+".json");
            if(!System.IO.File.Exists(path)||new FileInfo(path).Length>8192)return null;
            var release=JsonSerializer.Deserialize<PortableRelease>(System.IO.File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
            if(release is null||release.Platform!=platform||!release.PlatformTested||release.File!=Path.GetFileName(release.File)
                ||!System.Text.RegularExpressions.Regex.IsMatch(release.Sha256,"^[a-f0-9]{64}$"))return null;
            var localPreview=http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
                && config.GetValue<bool>("PortableInstaller:AllowLocalPreview")
                && http.Connection.RemoteIpAddress is {} address && System.Net.IPAddress.IsLoopback(address)
                && http.Request.Host.Host is "localhost" or "127.0.0.1" or "[::1]";
            if(!(release.Signed&&release.Channel=="stable")&&!(localPreview&&release.Channel=="private-validation"))return null;
            return System.IO.File.Exists(Path.Combine(directory,release.File))?release:null;
        }
        catch(Exception ex) when(ex is IOException or JsonException or ArgumentException){return null;}
    }
    public static string? VerifiedPath(HttpContext http,PortableRelease release)
    {
        var directory=http.RequestServices.GetRequiredService<IConfiguration>()["PortableInstaller:ReleaseDirectory"]!;
        var path=Path.Combine(directory,release.File);
        using var stream=System.IO.File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream))==release.Sha256?path:null;
    }
}

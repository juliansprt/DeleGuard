using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Deleguard.Api.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Deleguard.IntegrationTests;

public sealed class PortableReleaseTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "deleguard-release-test-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Production", true, true, true, "stable", "127.0.0.1", true)]
    [InlineData("Production", true, false, true, "stable", "127.0.0.1", false)]
    [InlineData("Production", true, false, true, "private-validation", "127.0.0.1", false)]
    [InlineData("Development", true, false, true, "private-validation", "127.0.0.1", true)]
    [InlineData("Development", false, false, true, "private-validation", "127.0.0.1", false)]
    [InlineData("Development", true, false, true, "private-validation", "192.0.2.8", false)]
    [InlineData("Development", true, false, false, "private-validation", "127.0.0.1", false)]
    [InlineData("Production", true, true, false, "stable", "127.0.0.1", false)]
    public void Publication_requires_operator_attested_signing_and_native_validation(string environment, bool preview, bool signed, bool tested, string channel, string remote, bool available)
    {
        var release = WriteRelease(signed, tested, channel);
        using var services = Services(environment, preview);
        var http = Context(services, remote);
        Assert.Equal(available, PortableReleases.Find(http, "win-x64") is not null);
        Assert.Null(PortableReleases.Find(http, "../win-x64"));
        Assert.Null(PortableReleases.Find(http, "osx-arm64"));
        if (available) Assert.Equal(Path.Combine(directory, release.File), PortableReleases.VerifiedPath(http, release));
    }

    [Fact]
    public void Changed_archive_and_foreign_file_paths_are_rejected()
    {
        var release = WriteRelease(true, true, "stable");
        using var services = Services("Production", false); var http = Context(services, "127.0.0.1");
        File.WriteAllText(Path.Combine(directory, release.File), "changed archive");
        Assert.Null(PortableReleases.VerifiedPath(http, release));
        File.WriteAllText(Path.Combine(directory, "win-x64.json"), JsonSerializer.Serialize(release with { File = "../foreign.zip" }));
        Assert.Null(PortableReleases.Find(http, "win-x64"));
    }

    private PortableRelease WriteRelease(bool signed, bool tested, string channel)
    {
        Directory.CreateDirectory(directory); var archive = Path.Combine(directory, "installer.zip");
        File.WriteAllText(archive, "test package");
        var release = new PortableRelease("win-x64", "4.0.0", "installer.zip", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))), signed, tested, channel);
        File.WriteAllText(Path.Combine(directory, "win-x64.json"), JsonSerializer.Serialize(release)); return release;
    }
    private ServiceProvider Services(string environment, bool preview) => new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["PortableInstaller:ReleaseDirectory"] = directory, ["PortableInstaller:AllowLocalPreview"] = preview.ToString() }).Build())
        .AddSingleton<IWebHostEnvironment>(new TestEnvironment { EnvironmentName = environment }).BuildServiceProvider();
    private static DefaultHttpContext Context(IServiceProvider services, string remote)
    {
        var http = new DefaultHttpContext { RequestServices = services }; http.Request.Host = new HostString("127.0.0.1", 5086);
        http.Connection.RemoteIpAddress = IPAddress.Parse(remote); return http;
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "ReleaseTest";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

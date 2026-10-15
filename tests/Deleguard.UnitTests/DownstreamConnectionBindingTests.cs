using Deleguard.McpGuard;

namespace Deleguard.UnitTests;

public sealed class DownstreamConnectionBindingTests
{
    [Fact]
    public void Effective_credentials_destination_and_working_directory_change_the_binding()
    {
        var options = new DownstreamMcpOptions { Name = "test", Transport = "stdio", Command = "node", Arguments = ["server.js"] };
        var environment = new Dictionary<string, string?> { ["GITHUB_TOKEN"] = "fixture-account-a", ["API_URL"] = "https://first.example" };
        string Hash(string cwd = "/first") => DownstreamMcpClient.ComputeConnectionHash(options, environment, cwd);
        var original = Hash();
        environment["GITHUB_TOKEN"] = "fixture-account-b";
        Assert.NotEqual(original, Hash());
        environment["GITHUB_TOKEN"] = "fixture-account-a";
        environment["API_URL"] = "https://second.example";
        Assert.NotEqual(original, Hash());
        environment["API_URL"] = "https://first.example";
        Assert.NotEqual(original, Hash("/second"));
        Assert.Equal(original, Hash());
    }
}

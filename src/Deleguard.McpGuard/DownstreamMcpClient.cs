using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Deleguard.McpGuard;

public sealed class DownstreamMcpClient : IDownstreamMcp, IAsyncDisposable
{
    private readonly McpClient _client;

    private DownstreamMcpClient(string name, string connectionHash, McpClient client)
    {
        Name = name;
        ConnectionHash = connectionHash;
        _client = client;
    }

    public string Name { get; }
    public string ConnectionHash { get; }

    public static async Task<DownstreamMcpClient> ConnectAsync(DownstreamMcpOptions options, CancellationToken cancellationToken)
    {
        var isHttp = options.Transport.Equals("http", StringComparison.OrdinalIgnoreCase);
        // Freeze exactly the process environment supplied to stdio. Inheritance after hashing is disabled.
        var effectiveEnvironment = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (!isHttp)
        {
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                effectiveEnvironment[(string)entry.Key] = entry.Value?.ToString();
            foreach (var entry in options.Env ?? []) effectiveEnvironment[entry.Key] = entry.Value;
        }
        var workingDirectory = isHttp ? "" : Path.GetFullPath(Environment.CurrentDirectory);
        var fingerprint = ComputeConnectionHash(options, effectiveEnvironment, workingDirectory);
        IClientTransport transport = isHttp
            ? new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(options.Url ?? throw new InvalidOperationException($"Downstream '{options.Name}' is missing url.")),
                Name = options.Name,
                AdditionalHeaders = options.Headers
            })
            : new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = options.Name,
                Command = options.Command ?? throw new InvalidOperationException($"Downstream '{options.Name}' is missing command."),
                Arguments = options.Arguments ?? [],
                EnvironmentVariables = effectiveEnvironment,
                InheritEnvironmentVariables = false,
                WorkingDirectory = workingDirectory
            });

        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        return new DownstreamMcpClient(options.Name, fingerprint, client);
    }

    public static string ComputeConnectionHash(DownstreamMcpOptions options, IReadOnlyDictionary<string, string?> effectiveEnvironment, string workingDirectory)
    {
        var bound = new { options, effectiveEnvironment, workingDirectory };
        var canonical = DeleguardPdpClient.CanonicalJson(JsonSerializer.SerializeToElement(bound));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public async Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        return tools.Select(tool => new DownstreamTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.JsonSchema
        }).ToList();
    }

    public async Task<DownstreamCallResult> CallAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        Dictionary<string, object?>? args = null;
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            args = JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.GetRawText());
        }

        var result = await _client.CallToolAsync(toolName, args, cancellationToken: cancellationToken);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        return new DownstreamCallResult
        {
            IsError = result.IsError == true,
            Text = text,
            Structured = result.StructuredContent
        };
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}

public sealed class DownstreamMcpHub : IAsyncDisposable
{
    private readonly Dictionary<string, IDownstreamMcp> _servers;

    public DownstreamMcpHub(IEnumerable<IDownstreamMcp> servers) =>
        _servers = servers.ToDictionary(server => server.Name, StringComparer.Ordinal);

    public IDownstreamMcp? Get(string name) => _servers.GetValueOrDefault(name);

    public IReadOnlyCollection<string> Names => _servers.Keys;

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers.Values.OfType<IAsyncDisposable>())
        {
            await server.DisposeAsync();
        }
    }
}

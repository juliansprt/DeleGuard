using System.Text.Json;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Deleguard.McpGuard;

public static class GuardHost
{
    public static async Task Main(string[] args)
    {
        var http = args.Any(arg => string.Equals(arg, "--http", StringComparison.OrdinalIgnoreCase));
        if (http)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Configuration.AddEnvironmentVariables();
            AddGuard(builder.Services, builder.Configuration);
            builder.Services.AddMcpServer(options => ConfigureMcp(options))
                .WithHttpTransport(transport => transport.SessionMode = HttpServerSessionMode.Stateless);

            var app = builder.Build();
            var httpToken = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<GuardOptions>>().Value.HttpToken;
            if (string.IsNullOrWhiteSpace(httpToken))
            {
                throw new InvalidOperationException(GuardHttpAuth.MissingTokenMessage);
            }

            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/mcp") &&
                    !GuardHttpAuth.Accepts(context.Request.Headers.Authorization.ToString(), httpToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized. Send Authorization: Bearer <Deleguard__HttpToken>.");
                    return;
                }

                await next();
            });
            app.MapMcp("/mcp");
            await app.RunAsync();
            return;
        }

        var host = Host.CreateApplicationBuilder(args);
        host.Configuration.AddEnvironmentVariables();
        host.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        AddGuard(host.Services, host.Configuration);
        host.Services.AddMcpServer(options => ConfigureMcp(options))
            .WithStdioServerTransport();
        await host.Build().RunAsync();
    }

    private static void AddGuard(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GuardOptions>(configuration.GetSection(GuardOptions.SectionName));
        services.AddHttpClient("deleguard-pdp", (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GuardOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.Url))
            {
                client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
            }

            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<DeleguardPdpClient>(sp =>
            new DeleguardPdpClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("deleguard-pdp"),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GuardOptions>>()));
        services.AddSingleton<IDeleguardPdp>(sp => sp.GetRequiredService<DeleguardPdpClient>());
        services.AddSingleton<IGuardConfigSource>(sp => sp.GetRequiredService<DeleguardPdpClient>());
        services.AddSingleton(sp =>
        {
            var configured = configuration.GetSection("DownstreamMcps").Get<List<DownstreamMcpOptions>>() ?? [];
            var clients = new List<IDownstreamMcp>();
            foreach (var item in configured.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
            {
                try
                {
                    clients.Add(DownstreamMcpClient.ConnectAsync(item, CancellationToken.None).GetAwaiter().GetResult());
                }
                catch (Exception ex)
                {
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger("Deleguard.McpGuard")
                        .LogError(ex, "Failed to connect downstream MCP {Name}. Calls that map to it will not forward.", item.Name);
                }
            }

            return new DownstreamMcpHub(clients);
        });
        services.AddSingleton<GuardEnforcer>();
    }

    private static void ConfigureMcp(McpServerOptions options)
    {
        options.ServerInfo = new Implementation
        {
            Name = "deleguard-guard",
            Version = "1.0.0"
        };
        options.Handlers.ListToolsHandler = async (context, cancellationToken) =>
        {
            var enforcer = context.Services!.GetRequiredService<GuardEnforcer>();
            var tools = await enforcer.ListToolsAsync(cancellationToken);
            return new ListToolsResult
            {
                Tools = tools.Select(tool => new Tool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = ToSchema(tool.InputSchema)
                }).ToList()
            };
        };
        options.Handlers.CallToolHandler = async (context, cancellationToken) =>
        {
            var enforcer = context.Services!.GetRequiredService<GuardEnforcer>();
            var name = context.Params?.Name ?? string.Empty;
            if (string.Equals(name, "approve", StringComparison.Ordinal) ||
                string.Equals(name, "deny", StringComparison.Ordinal))
            {
                var blocked = JsonSerializer.SerializeToElement(new
                {
                    result_type = "complete",
                    decision = "DENY",
                    forwarded = false,
                    reason = "Guard cannot approve or deny."
                });
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = blocked.GetRawText() }],
                    StructuredContent = blocked
                };
            }

            var args = context.Params?.Arguments is { } json
                ? JsonSerializer.SerializeToElement(json)
                : JsonSerializer.SerializeToElement(new { });
            var result = await enforcer.CallAsync(name, args, cancellationToken);
            return new CallToolResult
            {
                IsError = result.IsError,
                Content = [new TextContentBlock { Text = result.Text }],
                StructuredContent = result.Structured
            };
        };
    }

    private static JsonElement ToSchema(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object ? schema : JsonSerializer.SerializeToElement(new { type = "object" });
}

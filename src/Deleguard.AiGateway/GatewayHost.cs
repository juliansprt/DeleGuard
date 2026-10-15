using Microsoft.Extensions.Options;

namespace Deleguard.AiGateway;

public static class GatewayHost
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddEnvironmentVariables();
        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.AddHttpClient("deleguard-pdp", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.Url))
            {
                client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
            }

            client.Timeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddHttpClient("provider", client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        builder.Services.AddSingleton<DeleguardControlPlaneClient>(sp =>
            new DeleguardControlPlaneClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("deleguard-pdp"),
                sp.GetRequiredService<IOptions<GatewayOptions>>()));
        builder.Services.AddSingleton<ExactResponseCache>();
        builder.Services.AddSingleton<GatewayPipeline>();

        var app = builder.Build();
        app.MapPost("/v1/chat/completions", async (HttpRequest request, GatewayPipeline pipeline, CancellationToken cancellationToken) =>
        {
            var node = await System.Text.Json.Nodes.JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken)
                       ?? throw new InvalidOperationException("Request body required.");
            return await pipeline.ChatAsync(node, cancellationToken, GatewayAuth.ReadBearer(request), request.Headers["Idempotency-Key"].FirstOrDefault());
        });
        app.MapPost("/v1/embeddings", async (HttpRequest request, GatewayPipeline pipeline, CancellationToken cancellationToken) =>
        {
            var node = await System.Text.Json.Nodes.JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken)
                       ?? throw new InvalidOperationException("Request body required.");
            return await pipeline.EmbedAsync(node, cancellationToken, GatewayAuth.ReadBearer(request), request.Headers["Idempotency-Key"].FirstOrDefault());
        });
        app.MapGet("/v1/models", (HttpRequest request, GatewayPipeline pipeline, CancellationToken cancellationToken) =>
            pipeline.ModelsAsync(cancellationToken, GatewayAuth.ReadBearer(request)));
        app.MapGet("/health", (IConfiguration configuration, IOptions<GatewayOptions> options) =>
            Results.Json(GatewayHealth.Snapshot(configuration, options.Value), DeleguardControlPlaneClient.Json));
        await app.RunAsync();
    }
}

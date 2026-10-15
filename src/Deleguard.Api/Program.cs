using System.Text.Json;
using Deleguard.Api.Http;
using Deleguard.Api.Mcp;
using Deleguard.Infrastructure;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsProduction())
{
    ProductionConfiguration.Validate(builder.Configuration);
    if (!builder.Configuration.GetValue<bool>("Oidc:Enabled") ||
        !Uri.TryCreate(builder.Configuration["Oidc:Authority"], UriKind.Absolute, out var authority) || authority.Scheme != "https" ||
        !builder.Configuration.GetValue<bool>("Oidc:RequireVerifiedEmail") || string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeyPath"]))
        throw new InvalidOperationException("Production requires HTTPS identity, verified email, and persistent data-protection keys.");
}
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1048576);
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var cidr in builder.Configuration.GetSection("TrustedProxyNetworks").Get<string[]>() ?? [])
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddDeleguardInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Deleguard.Application.Abstractions.IAdminOperationContext, AdminOperationContext>();
var dataProtectionPath = builder.Configuration["DataProtection:KeyPath"];
if (string.IsNullOrWhiteSpace(dataProtectionPath))
{
    dataProtectionPath = Path.Combine(builder.Environment.ContentRootPath, "dpkeys");
}

Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .SetApplicationName("Deleguard")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
builder.AddDeleguardOidc();
// SDK 2.2.0 still speaks initialize (2025) plus 2026-07-28. MapMcp() would own /mcp and
// bypass the dgm_ middleware + in-process PDP envelope, so the custom dispatcher stays.
builder.Services.AddMcpServer().WithHttpTransport();
builder.Services.AddRazorPages();
builder.Services.AddSingleton<Deleguard.Api.Localization.UiText>();
builder.Services.AddScoped<Deleguard.Api.Pages.ConsoleChrome>();
builder.Services.AddScoped<Deleguard.Api.Mcp.AdminMcpDispatcher>();
builder.Services.AddScoped<Deleguard.Api.Mcp.RemoteGuardMcpDispatcher>();
builder.Services.Configure<Deleguard.Api.Mcp.RemoteGuardOptions>(builder.Configuration.GetSection("RemoteMcp"));
builder.Services.AddHttpClient("remote-mcp-guard", client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => Deleguard.Infrastructure.Security.SsrfSafeWebhookClient.CreateHandler(new Deleguard.Infrastructure.Security.WebhookOptions { AllowLoopback = false, RequestTimeoutSeconds = 15 }));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres") ?? string.Empty);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("deleguard-api"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

var app = builder.Build();

app.UseForwardedHeaders();
if (app.Environment.IsProduction()) app.UseHsts();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStaticFiles();
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/v1") && !context.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseRequestLocalization(Deleguard.Api.Localization.UiText.Options()));
app.Use(async (context, next) => { context.Request.EnableBuffering(); await next(); });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<DeleguardAuthMiddleware>();
app.UseMiddleware<ProductApplicationMiddleware>();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapDeleguardApi();
app.MapIdeApi();
app.MapExecutionApi();
app.MapAdminMcp();
app.MapRemoteGuardMcp();
app.MapRazorPages();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
    if (app.Environment.IsDevelopment() || args.Contains("--migrate-only", StringComparer.Ordinal))
        await db.Database.MigrateAsync();
    else if ((await db.Database.GetPendingMigrationsAsync()).Any())
        throw new InvalidOperationException("Pending database migration. Run the controlled migration job before starting this revision.");
    if (app.Environment.IsDevelopment())
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync(CancellationToken.None);

}

if (!args.Contains("--migrate-only", StringComparer.Ordinal)) app.Run();

public partial class Program;

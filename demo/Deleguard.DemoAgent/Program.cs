using Deleguard.DemoAgent.Agent;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (!string.IsNullOrWhiteSpace(openaiKey))
{
    builder.Configuration["Llm:ApiKey"] = openaiKey;
}

var deleguardUrl = Environment.GetEnvironmentVariable("DELEGUARD_URL");
if (!string.IsNullOrWhiteSpace(deleguardUrl))
{
    builder.Configuration["Deleguard:Url"] = deleguardUrl;
}

var agentKey = Environment.GetEnvironmentVariable("DELEGUARD_AGENT_KEY")
               ?? Environment.GetEnvironmentVariable("DELEGUARD_TOKEN");
if (!string.IsNullOrWhiteSpace(agentKey))
{
    builder.Configuration["Deleguard:AgentKey"] = agentKey;
}

builder.Services.Configure<DeleguardOptions>(builder.Configuration.GetSection(DeleguardOptions.SectionName));
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.AddSingleton<IDemoInvoiceStore, DemoInvoiceStore>();
builder.Services.AddSingleton<ChatTranscript>();
builder.Services.AddSingleton<StubIntentInducer>();
builder.Services.AddSingleton<OpenAiCompatibleInducer>();
builder.Services.AddSingleton<IIntentInducer, CompositeIntentInducer>();
builder.Services.AddScoped<DemoAgentHandler>();
builder.Services.AddRazorPages();
builder.Services.AddHttpClient("llm", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient<IDeleguardClient, DeleguardHttpClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<DeleguardOptions>>().Value;
    client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();
var llmEnabled = !string.IsNullOrWhiteSpace(app.Configuration["Llm:ApiKey"]);
app.Logger.LogInformation(
    "Demo induction mode: {Mode} ({Model})",
    llmEnabled ? "LLM" : "stub",
    app.Configuration["Llm:Model"] ?? "none");
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();

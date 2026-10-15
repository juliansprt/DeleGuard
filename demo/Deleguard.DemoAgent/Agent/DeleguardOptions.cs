namespace Deleguard.DemoAgent.Agent;

public sealed class DeleguardOptions
{
    public const string SectionName = "Deleguard";

    public string Url { get; set; } = "http://localhost:5080";
    public string ConsoleUrl { get; set; } = "http://localhost:5080";
    public string AgentKey { get; set; } = "dgk_demo_invoice_agent_local_dev_only";
    public string AgentId { get; set; } = "invoice-agent";
    public string Environment { get; set; } = "production";
}

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
}

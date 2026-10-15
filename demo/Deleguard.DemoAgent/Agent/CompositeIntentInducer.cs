using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deleguard.DemoAgent.Agent;

public sealed class CompositeIntentInducer : IIntentInducer
{
    private readonly IIntentInducer _stub;
    private readonly OpenAiCompatibleInducer _llm;
    private readonly LlmOptions _options;
    private readonly ILogger<CompositeIntentInducer> _logger;

    public CompositeIntentInducer(
        StubIntentInducer stub,
        OpenAiCompatibleInducer llm,
        IOptions<LlmOptions> options,
        ILogger<CompositeIntentInducer> logger)
    {
        _stub = stub;
        _llm = llm;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InducedIntent> InduceAsync(string userText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return await _stub.InduceAsync(userText, cancellationToken);
        }

        try
        {
            _logger.LogInformation("Inducing intent with OpenAI-compatible LLM {Model}.", _options.Model);
            var llm = await _llm.InduceAsync(userText, cancellationToken);
            var stub = await _stub.InduceAsync(userText, cancellationToken);
            return Coalesce(llm, stub);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM induction failed; falling back to stub.");
            var fallback = await _stub.InduceAsync(userText, cancellationToken);
            return fallback with { Source = "stub-fallback" };
        }
    }

    public static InducedIntent Coalesce(InducedIntent primary, InducedIntent fallback)
    {
        var resourceId = primary.ResourceId;
        if (!string.IsNullOrWhiteSpace(fallback.ResourceId) &&
            (string.IsNullOrWhiteSpace(primary.ResourceId) ||
             !string.Equals(primary.ResourceId, fallback.ResourceId, StringComparison.OrdinalIgnoreCase)))
        {
            resourceId = fallback.ResourceId;
        }

        var action = primary.Action;
        if (!IsCancel(action) && IsCancel(fallback.Action) && !string.IsNullOrWhiteSpace(resourceId))
        {
            action = fallback.Action;
        }

        return primary with { Action = action, ResourceId = resourceId };
    }

    private static bool IsCancel(string? action) =>
        string.Equals(action, DemoAgentHandler.CancelAction, StringComparison.OrdinalIgnoreCase);
}

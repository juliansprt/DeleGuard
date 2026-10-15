namespace Deleguard.AiGateway;

public static class GatewayHttp
{
    public const string DecisionHeader = "X-Deleguard-Decision";
    public const string ModelHeader = "X-Deleguard-Model";
    public const string CacheHeader = "X-Deleguard-Cache";
    public const string RouteReasonHeader = "X-Deleguard-Route-Reason";

    public static IResult WithHeaders(
        IResult inner,
        string decision,
        string? model = null,
        string cache = "miss",
        string? routeReason = null) =>
        new GatewayHeaderResult(inner, decision, model, cache, routeReason);

    public static IResult Unauthenticated() =>
        WithHeaders(
            Results.Json(
                new
                {
                    error = new
                    {
                        type = "deleguard_unauthenticated",
                        message = "Agent API key required. Send Authorization: Bearer dgk_…"
                    }
                },
                statusCode: StatusCodes.Status401Unauthorized),
            "UNAUTHENTICATED");
}

internal sealed class GatewayHeaderResult : IResult
{
    private readonly IResult _inner;
    private readonly string _decision;
    private readonly string? _model;
    private readonly string _cache;
    private readonly string? _routeReason;

    public GatewayHeaderResult(IResult inner, string decision, string? model, string cache, string? routeReason)
    {
        _inner = inner;
        _decision = decision;
        _model = model;
        _cache = cache;
        _routeReason = routeReason;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers[GatewayHttp.DecisionHeader] = _decision;
        if (!string.IsNullOrWhiteSpace(_model))
        {
            httpContext.Response.Headers[GatewayHttp.ModelHeader] = _model;
        }

        httpContext.Response.Headers[GatewayHttp.CacheHeader] = _cache;
        if (!string.IsNullOrWhiteSpace(_routeReason))
        {
            httpContext.Response.Headers[GatewayHttp.RouteReasonHeader] = _routeReason;
        }

        await _inner.ExecuteAsync(httpContext);
    }
}

using Deleguard.Application;

namespace Deleguard.Api.Http;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var (status, code) = ex switch
            {
                RequestValidationException => (StatusCodes.Status400BadRequest, "invalid_request"),
                AgentAuthenticationException => (StatusCodes.Status401Unauthorized, "unauthorized"),
                ForbiddenException => (StatusCodes.Status403Forbidden, "forbidden"),
                NotFoundException => (StatusCodes.Status404NotFound, "not_found"),
                IdempotencyConflictException or ConflictException => (StatusCodes.Status409Conflict, "conflict"),
                Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrent_change"),
                Npgsql.PostgresException p when p.SqlState is "40001" or "40P01" or "P0001" => (StatusCodes.Status409Conflict, "concurrent_change"),
                Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Npgsql.PostgresException p } when p.SqlState is "40001" or "40P01" or "P0001" => (StatusCodes.Status409Conflict, "concurrent_change"),
                FailClosedException => (StatusCodes.Status503ServiceUnavailable, "authorization_unavailable"),
                _ => (StatusCodes.Status500InternalServerError, "internal_error")
            };

            if (status >= 500)
            {
                _logger.LogError(ex, "Authorization path failed closed.");
            }

            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.StatusCode = status;
            var message = status >= 500 ? "The service could not complete the operation. Use the request identifier to investigate before retrying." :
                code == "concurrent_change" ? "Security state changed or permission expired. Refresh the decision; do not replay a consumed operation." : ex.Message;
            await context.Response.WriteAsJsonAsync(new { error = code, message, request_id = context.TraceIdentifier });
        }
    }
}

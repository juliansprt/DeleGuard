namespace Deleguard.Application;

public sealed class FailClosedException : Exception
{
    public FailClosedException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class AgentAuthenticationException : Exception
{
    public AgentAuthenticationException(string message) : base(message) { }
}

public sealed class RequestValidationException : Exception
{
    public RequestValidationException(string message) : base(message) { }
}

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string message) : base(message) { }
}

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

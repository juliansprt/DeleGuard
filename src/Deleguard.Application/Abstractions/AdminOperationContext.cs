namespace Deleguard.Application.Abstractions;

/// <summary>Binds the complete transport command, not only policy-selected facts.</summary>
public interface IAdminOperationContext
{
    Task<OperationRef> ReadAsync(CancellationToken cancellationToken);
}

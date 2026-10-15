using Deleguard.Application.Abstractions;

namespace Deleguard.Infrastructure.Persistence;

public sealed class TenantContext : ITenantContext
{
    public string OrganizationId { get; set; } = "__unset__";
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly DeleguardDbContext _db;

    public EfUnitOfWork(DeleguardDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null) return await action();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action();
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

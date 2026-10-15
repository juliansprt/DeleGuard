using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Deleguard.Infrastructure.Persistence;

public sealed class DeleguardDbContextFactory : IDesignTimeDbContextFactory<DeleguardDbContext>
{
    public DeleguardDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DeleguardDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=deleguard;Username=deleguard;Password=deleguard")
            .Options;
        return new DeleguardDbContext(options);
    }
}

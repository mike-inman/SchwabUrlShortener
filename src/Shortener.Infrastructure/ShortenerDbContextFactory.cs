using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shortener.Infrastructure;

// Used only by explicit dotnet-ef migration commands, never by API startup.
public sealed class ShortenerDbContextFactory : IDesignTimeDbContextFactory<ShortenerDbContext>
{
    public ShortenerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings__ShortenerDb must be set for migration tooling.");
        var options = new DbContextOptionsBuilder<ShortenerDbContext>().UseNpgsql(connectionString).Options;
        return new ShortenerDbContext(options);
    }
}

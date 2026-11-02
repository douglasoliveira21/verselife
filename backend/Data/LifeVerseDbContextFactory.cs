using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LifeVerse.Api.Data;

public sealed class LifeVerseDbContextFactory : IDesignTimeDbContextFactory<LifeVerseDbContext>
{
    public LifeVerseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=lifeverse;Username=lifeverse;Password=development-only";
        var options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(connectionString).Options;
        return new LifeVerseDbContext(options);
    }
}

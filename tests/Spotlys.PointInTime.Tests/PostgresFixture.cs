using Microsoft.EntityFrameworkCore;
using Spotlys.Infrastructure;
using Testcontainers.PostgreSql;

namespace Spotlys.PointInTime.Tests;

// Real Postgres 17 in a container, migrated with the project's own EF migrations -- same
// shape as Spotlys.Integration.Tests.PostgresFixture, duplicated rather than shared across
// projects since it's 15 lines and this project's whole point (docs/ARCHITECTURE.md §2's
// solution layout names it as its own top-level test project) is to stand alone.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public SpotlysDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SpotlysDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new SpotlysDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

using Microsoft.EntityFrameworkCore;
using Spotlys.Infrastructure;
using Testcontainers.PostgreSql;

namespace Spotlys.Integration.Tests;

// Real Postgres 17 in a container, migrated with the project's own EF migrations -- "real
// SQL, real migrations, real timezone behaviour" (docs/ENGINEERING.md §2), not an
// in-memory provider standing in for Postgres.
#pragma warning disable CA1515 // must be public: xUnit injects IClassFixture<T> by constructor
public sealed class PostgresFixture : IAsyncLifetime
#pragma warning restore CA1515
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    /// <summary>For tests that need to build their own DI container against the same real
    /// Postgres instance (e.g. an Identity <c>UserManager</c>), not just a bare DbContext.</summary>
    public string ConnectionString => _container.GetConnectionString();

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

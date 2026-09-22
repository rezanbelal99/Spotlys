using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spotlys.Application.Accounts;
using Spotlys.Application.Ingestion;
using Spotlys.Application.Metering;
using Spotlys.Domain.Pricing;
using Spotlys.Infrastructure;
using Spotlys.Infrastructure.Accounts;
using Spotlys.Infrastructure.Metering;

namespace Spotlys.Integration.Tests.Accounts;

/// <summary>
/// Real Postgres, real cascade-delete behaviour, real retention purge -- docs/ARCHITECTURE.md
/// §9's "export and delete endpoints from day one" and docs/DEVOPS.md §7's retention-job
/// audit trail, verified against actual foreign-key constraints rather than assumed from the
/// migration's SQL text.
/// </summary>
public sealed class AccountDeletionAndRetentionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SpotlysDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<SpotlysDbContext>();
        services.AddScoped<IMeterProfileRepository, MeterProfileRepository>();
        services.AddScoped<IConsumptionReadingRepository, ConsumptionReadingRepository>();
        services.AddScoped<IConsumptionRetentionPurgeWriter, ConsumptionRetentionPurgeRepository>();
        services.AddScoped<IIngestionRunWriter, NullIngestionRunWriter>();
        services.AddSingleton(TimeProvider.System);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Deleting_the_account_cascades_through_meter_profile_and_consumption_reading()
    {
        await using var provider = BuildServices();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpotlysDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var meters = scope.ServiceProvider.GetRequiredService<IMeterProfileRepository>();
        var consumption = scope.ServiceProvider.GetRequiredService<IConsumptionReadingRepository>();

        var user = new AppUser { UserName = $"cascade-{Guid.NewGuid():N}@example.test", Email = $"cascade-{Guid.NewGuid():N}@example.test", CreatedAtUtc = DateTimeOffset.UtcNow };
        var createResult = await userManager.CreateAsync(user, "Correct-Horse-Battery-9");
        Assert.True(createResult.Succeeded, string.Join(" ", createResult.Errors.Select(e => e.Description)));

        var meterId = Guid.CreateVersion7();
        await meters.CreateAsync(new MeterProfileEntry(
            meterId, user.Id, "NO2", "glitre-nett", "stromstotte", false, 0m, 0m, 3, DateTimeOffset.UtcNow), CancellationToken.None);

        var readings = Enumerable.Range(0, 24)
            .Select(h => new HourlyConsumption(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(h), 1.5m))
            .ToList();
        await consumption.UpsertRangeAsync(meterId, readings, "csv_import", CancellationToken.None);

        // Sanity check: the data really is there before deletion.
        Assert.NotNull(await meters.GetAsync(meterId, CancellationToken.None));
        Assert.Equal(24, (await consumption.GetAllAsync(meterId, CancellationToken.None)).Count);

        var deleteResult = await userManager.DeleteAsync(user);
        Assert.True(deleteResult.Succeeded, string.Join(" ", deleteResult.Errors.Select(e => e.Description)));

        Assert.Null(await meters.GetAsync(meterId, CancellationToken.None));
        Assert.Empty(await consumption.GetAllAsync(meterId, CancellationToken.None));

        // The account itself is gone too -- not just orphaned.
        Assert.Null(await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id));
    }

    [Fact]
    public async Task Retention_purge_deletes_only_readings_older_than_the_meters_own_cutoff_and_writes_an_audit_row()
    {
        await using var provider = BuildServices();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpotlysDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var meters = scope.ServiceProvider.GetRequiredService<IMeterProfileRepository>();
        var consumption = scope.ServiceProvider.GetRequiredService<IConsumptionReadingRepository>();
        var purgeWriter = scope.ServiceProvider.GetRequiredService<IConsumptionRetentionPurgeWriter>();
        var runWriter = scope.ServiceProvider.GetRequiredService<IIngestionRunWriter>();

        var user = new AppUser { UserName = $"retention-{Guid.NewGuid():N}@example.test", Email = $"retention-{Guid.NewGuid():N}@example.test", CreatedAtUtc = DateTimeOffset.UtcNow };
        await userManager.CreateAsync(user, "Correct-Horse-Battery-9");

        var meterId = Guid.CreateVersion7();
        const int retentionYears = 1;
        await meters.CreateAsync(new MeterProfileEntry(
            meterId, user.Id, "NO2", "glitre-nett", "stromstotte", false, 0m, 0m, retentionYears, DateTimeOffset.UtcNow), CancellationToken.None);

        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var oldReading = new HourlyConsumption(now.AddYears(-2), 1m); // well outside a 1-year retention
        var recentReading = new HourlyConsumption(now.AddDays(-1), 1m); // well inside it
        await consumption.UpsertRangeAsync(meterId, [oldReading, recentReading], "csv_import", CancellationToken.None);

        var fixedTime = new FixedTimeProvider(now);
        var useCase = new PurgeExpiredConsumptionUseCase(meters, consumption, purgeWriter, runWriter, fixedTime);
        var result = await useCase.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Rows);

        var remaining = await consumption.GetAllAsync(meterId, CancellationToken.None);
        Assert.Single(remaining);
        Assert.Equal(recentReading.HourStartUtc, remaining[0].HourStartUtc);

        var purgeRecord = await db.ConsumptionRetentionPurges.FirstOrDefaultAsync(p => p.MeterProfileId == meterId);
        Assert.NotNull(purgeRecord);
        Assert.Equal(1, purgeRecord!.RowsDeleted);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Discards ingestion_run writes -- this test suite cares about the retention
    /// purge's own side effects, not the shared ingestion_run ledger.</summary>
    private sealed class NullIngestionRunWriter : IIngestionRunWriter
    {
        public Task RecordAsync(IngestionRunEntry entry, CancellationToken ct) => Task.CompletedTask;
    }
}

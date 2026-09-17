using Spotlys.Domain.Pricing;
using Spotlys.Infrastructure.Pricing;

namespace Spotlys.Integration.Tests.Pricing;

public sealed class PriceObservationRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static PriceObservation Observation(DateTimeOffset hourStart, decimal price) =>
        new(PriceArea.NO2, hourStart, price, 11.5m, "hkso", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Upserting_the_same_natural_key_twice_updates_rather_than_duplicates()
    {
        // The whole point of upsert-on-natural-key (docs/DATA.md §6): re-running an
        // ingestion window must be safe, not create duplicate rows.
        await using var dbContext = fixture.CreateDbContext();
        var repository = new PriceObservationRepository(dbContext);
        var hourStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        await repository.UpsertRangeAsync([Observation(hourStart, 100m)], CancellationToken.None);
        await repository.UpsertRangeAsync([Observation(hourStart, 150m)], CancellationToken.None);

        var range = await repository.GetRangeAsync(
            PriceArea.NO2, hourStart, hourStart.AddHours(1), CancellationToken.None);

        var single = Assert.Single(range);
        Assert.Equal(150m, single.PriceExVatOrePerKwh);
    }

    [Fact]
    public async Task GetRange_excludes_hours_outside_the_requested_window()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new PriceObservationRepository(dbContext);
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

        await repository.UpsertRangeAsync(
            [
                Observation(day.AddHours(-1), 1m), // just before the window
                Observation(day, 2m),
                Observation(day.AddHours(23), 3m), // last hour still inside
                Observation(day.AddHours(24), 4m), // first hour of the next window
            ],
            CancellationToken.None);

        var range = await repository.GetRangeAsync(PriceArea.NO2, day, day.AddDays(1), CancellationToken.None);

        Assert.Equal(2, range.Count);
        Assert.All(range, r => Assert.InRange(r.HourStartUtc, day, day.AddHours(23)));
    }

    [Fact]
    public async Task Handles_a_DST_transition_day_with_23_hours()
    {
        // docs/ARCHITECTURE.md §3: the March day has no 02:00 -- 23 hours, not 24. A test
        // fixture for this is mandatory, not optional.
        await using var dbContext = fixture.CreateDbContext();
        var repository = new PriceObservationRepository(dbContext);

        // 2026-03-29 is the Europe/Oslo spring-forward transition (01:00 -> 03:00 CEST).
        var dayStartUtc = new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero); // 00:00 Oslo
        var observations = Enumerable.Range(0, 23)
            .Select(h => Observation(dayStartUtc.AddHours(h), 100m + h))
            .ToList();

        await repository.UpsertRangeAsync(observations, CancellationToken.None);

        var range = await repository.GetRangeAsync(
            PriceArea.NO2, dayStartUtc, dayStartUtc.AddHours(23), CancellationToken.None);

        Assert.Equal(23, range.Count);
    }
}

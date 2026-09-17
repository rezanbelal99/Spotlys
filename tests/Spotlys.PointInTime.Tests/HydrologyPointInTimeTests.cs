using Spotlys.Application.Hydrology;
using Spotlys.Infrastructure.Hydrology;

namespace Spotlys.PointInTime.Tests;

/// <summary>
/// Same guarantee as <see cref="WeatherForecastPointInTimeTests"/>, for
/// <see cref="HydrologyRepository.GetObservationAsOfAsync"/> (docs/FORECASTING.md §5).
///
/// Unlike weather_forecast (whose key includes issued_at_utc, so every model run is its own
/// row), hydrology_observation's natural key is (zone, week_start_date, source) --
/// matching price_observation's own established pattern (docs/DATA.md §1). A later fetch
/// for the same week upserts in place rather than adding a new row, so a revision's
/// *prior* value isn't recoverable. That's a completeness gap, not a leakage bug: these
/// tests confirm the method still never returns a row fetched after as_of, even though it
/// may honestly return null for a period a since-overwritten value would have covered
/// (fails safe -- missing rather than wrong -- exactly like price_observation already
/// accepts for ENTSO-E revisions).
/// </summary>
public sealed class HydrologyPointInTimeTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Zone = "NO2";
    private static readonly DateOnly WeekStart = new(2026, 6, 15);

    private static HydrologyObservationEntry Entry(DateTimeOffset fetchedAt, float fillFraction) =>
        new(Zone, WeekStart, fetchedAt, fillFraction, 30f, 15f, null, "nve-magasinstatistikk");

    [Fact]
    public async Task A_later_revision_is_invisible_to_an_as_of_before_it_was_fetched()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new HydrologyRepository(dbContext);

        var firstFetch = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);
        var revisionFetch = firstFetch.AddDays(3);

        // Two separate ingestion runs, exactly as IngestHydrologyUseCase would produce them
        // -- the second is a correction NVE published for the same week.
        await repository.UpsertObservationsAsync([Entry(firstFetch, 0.50f)], CancellationToken.None);
        await repository.UpsertObservationsAsync([Entry(revisionFetch, 0.99f)], CancellationToken.None);

        // As of right after the first fetch (before the revision existed), the table now
        // only holds the revised row -- which must be excluded, not silently substituted.
        var asOfBeforeRevision = firstFetch.AddHours(1);
        var resultBefore = await repository.GetObservationAsOfAsync(Zone, WeekStart, asOfBeforeRevision, CancellationToken.None);
        Assert.Null(resultBefore);

        // As of after the revision, it's correctly visible.
        var resultAfter = await repository.GetObservationAsOfAsync(Zone, WeekStart, revisionFetch.AddHours(1), CancellationToken.None);
        Assert.NotNull(resultAfter);
        Assert.Equal(0.99f, resultAfter.FillFraction);
        Assert.True(resultAfter.FetchedAtUtc <= revisionFetch.AddHours(1));
    }

    [Fact]
    public async Task Returns_null_when_the_reading_was_fetched_after_as_of()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new HydrologyRepository(dbContext);

        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await repository.UpsertObservationsAsync([Entry(asOf.AddDays(1), 0.5f)], CancellationToken.None);

        var result = await repository.GetObservationAsOfAsync(Zone, WeekStart, asOf, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Holds_for_many_random_historic_as_of_values_across_different_weeks()
    {
        // Each simulated week gets exactly one fetch (the realistic weekly-cadence case,
        // no same-key collisions), so this exercises the general "never returns a row
        // fetched after as_of" property across a real spread of data, matching
        // docs/FORECASTING.md §5's own description of the test.
        await using var dbContext = fixture.CreateDbContext();
        var repository = new HydrologyRepository(dbContext);

        var baseTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var weeks = Enumerable.Range(0, 20)
            .Select(i => (WeekStart: WeekStart.AddDays(i * 7), FetchedAt: baseTime.AddDays(i * 7)))
            .ToList();

        foreach (var week in weeks)
        {
            await repository.UpsertObservationsAsync(
                [new HydrologyObservationEntry(Zone, week.WeekStart, week.FetchedAt, 0.5f, 30f, 15f, null, "nve-magasinstatistikk")],
                CancellationToken.None);
        }

        var random = new Random(Seed: 20260615);

        for (var i = 0; i < 100; i++)
        {
            var week = weeks[random.Next(weeks.Count)];
            var offsetHours = (random.NextDouble() - 0.5) * 24 * 10; // +/- 5 days around its own fetch
            var asOf = week.FetchedAt.AddHours(offsetHours);

            var result = await repository.GetObservationAsOfAsync(Zone, week.WeekStart, asOf, CancellationToken.None);

            if (asOf < week.FetchedAt)
            {
                Assert.Null(result);
            }
            else
            {
                Assert.NotNull(result);
                Assert.True(result.FetchedAtUtc <= asOf,
                    $"leaked a reading fetched at {result.FetchedAtUtc}, after as_of {asOf}");
            }
        }
    }
}

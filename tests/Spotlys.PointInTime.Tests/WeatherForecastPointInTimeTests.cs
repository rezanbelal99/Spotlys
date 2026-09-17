using Spotlys.Application.Weather;
using Spotlys.Infrastructure.Weather;

namespace Spotlys.PointInTime.Tests;

/// <summary>
/// docs/FORECASTING.md §5: "A feature used for a forecast issued at time T may only depend
/// on information that existed at time T." <see cref="WeatherForecastRepository.GetAsOfAsync"/>
/// is the .NET-side building block that guarantee has to hold in -- these tests plant
/// deliberately future-issued rows alongside real-looking past ones and prove the method
/// never surfaces them.
/// </summary>
public sealed class WeatherForecastPointInTimeTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string PointId = "test-point";
    private static readonly DateTimeOffset ValidAt = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private async Task SeedPointAsync()
    {
        await using var dbContext = fixture.CreateDbContext();
        if (!dbContext.WeatherPoints.Any(p => p.Id == PointId))
        {
            dbContext.WeatherPoints.Add(new WeatherPointRow
            {
                Id = PointId,
                Zone = "NO2",
                Kind = "demand",
                Name = "Test point",
                Lat = 58m,
                Lon = 8m,
                AltitudeM = 10,
            });
            await dbContext.SaveChangesAsync();
        }
    }

    private static WeatherForecastEntry Entry(DateTimeOffset issuedAt, float tempC) =>
        new(PointId, issuedAt, issuedAt, ValidAt, tempC, null, null, null, null);

    [Fact]
    public async Task Never_returns_a_forecast_issued_after_as_of()
    {
        await SeedPointAsync();
        await using var dbContext = fixture.CreateDbContext();
        var repository = new WeatherForecastRepository(dbContext);

        // Three model runs for the same target hour: one well in the past relative to
        // as_of, one just before it, and one deliberately AFTER as_of -- the one that must
        // never be returned.
        var asOf = new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero);
        await repository.UpsertRangeAsync(
            [
                Entry(asOf.AddHours(-48), 10f),  // well before as_of
                Entry(asOf.AddHours(-6), 12f),   // freshest run still before as_of
                Entry(asOf.AddHours(6), 99f),    // issued AFTER as_of -- must be invisible
            ],
            CancellationToken.None);

        var result = await repository.GetAsOfAsync(PointId, ValidAt, asOf, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IssuedAtUtc <= asOf, $"leaked a forecast issued at {result.IssuedAtUtc}, after as_of {asOf}");
        // Must be the freshest of the two eligible runs, not just any eligible one.
        Assert.Equal(asOf.AddHours(-6), result.IssuedAtUtc);
        Assert.Equal(12f, result.TempC);
    }

    [Fact]
    public async Task Returns_null_when_every_forecast_was_issued_after_as_of()
    {
        await SeedPointAsync();
        await using var dbContext = fixture.CreateDbContext();
        var repository = new WeatherForecastRepository(dbContext);

        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await repository.UpsertRangeAsync([Entry(asOf.AddDays(1), 5f)], CancellationToken.None);

        var result = await repository.GetAsOfAsync(PointId, ValidAt, asOf, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Holds_for_many_random_historic_as_of_values()
    {
        // docs/FORECASTING.md §5's own test description: "for a set of random historic
        // as_of values, asserts no feature row references data with a later observation
        // time." A fixed seed keeps this reproducible while still exercising many points
        // across the seeded series, not just hand-picked boundary cases.
        await SeedPointAsync();
        await using var dbContext = fixture.CreateDbContext();
        var repository = new WeatherForecastRepository(dbContext);

        var baseTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var issuedTimes = Enumerable.Range(0, 40).Select(i => baseTime.AddHours(i * 6)).ToList();
        await repository.UpsertRangeAsync(
            issuedTimes.Select(t => Entry(t, (float)t.Hour)).ToList(), CancellationToken.None);

        var random = new Random(Seed: 20260615);
        var rangeStart = issuedTimes[0].AddHours(-24);
        var rangeEnd = issuedTimes[^1].AddHours(24);

        for (var i = 0; i < 200; i++)
        {
            var offsetHours = random.NextDouble() * (rangeEnd - rangeStart).TotalHours;
            var asOf = rangeStart.AddHours(offsetHours);

            var result = await repository.GetAsOfAsync(PointId, ValidAt, asOf, CancellationToken.None);

            var expectedIssuedAt = issuedTimes.Where(t => t <= asOf).Cast<DateTimeOffset?>().Max();

            Assert.Equal(expectedIssuedAt, result?.IssuedAtUtc);
            if (result is not null)
            {
                Assert.True(result.IssuedAtUtc <= asOf,
                    $"leaked a forecast issued at {result.IssuedAtUtc}, after as_of {asOf}");
            }
        }
    }
}

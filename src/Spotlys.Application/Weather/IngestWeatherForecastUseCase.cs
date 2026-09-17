using Spotlys.Application.Ingestion;

namespace Spotlys.Application.Weather;

/// <summary>
/// Fetches and upserts every seeded point's forecast for a zone (docs/DATA.md §6: bounded,
/// idempotent, honest on failure). Mirrors <c>IngestDayAheadPricesUseCase</c>'s shape.
/// </summary>
public sealed class IngestWeatherForecastUseCase(
    IWeatherPointRepository pointRepository,
    IWeatherForecastSource source,
    IWeatherForecastRepository forecastRepository,
    IIngestionRunWriter runWriter,
    TimeProvider timeProvider)
{
    /// <summary>The job_name recorded in ingestion_run for every run of this use case.</summary>
    public const string JobName = "IngestWeatherForecast";

    private const int HorizonHours = 168; // docs/FORECASTING.md §1: D+1..D+7

    /// <summary>Fetches and upserts every seeded point's forecast for <paramref name="zone"/>.</summary>
    public async Task<IngestionRunEntry> RunAsync(string zone, CancellationToken ct)
    {
        var startedAtUtc = timeProvider.GetUtcNow();
        var points = await pointRepository.ListForZoneAsync(zone, ct).ConfigureAwait(false);

        var entries = new List<WeatherForecastEntry>();
        var failures = new List<string>();

        foreach (var point in points)
        {
            var result = await source.FetchAsync(point, HorizonHours, ct).ConfigureAwait(false);
            if (result.Status == WeatherFetchStatus.Found)
            {
                entries.AddRange(result.Entries);
            }
            else if (result.Status == WeatherFetchStatus.Failed)
            {
                failures.Add($"{point.PointId}: {result.Error}");
            }
            // NotModified: still fresh, nothing to do -- not a failure.
        }

        if (entries.Count > 0)
        {
            await forecastRepository.UpsertRangeAsync(entries, ct).ConfigureAwait(false);
        }

        var status = failures.Count == 0
            ? IngestionRunStatus.Ok
            : entries.Count > 0
                ? IngestionRunStatus.Partial
                : IngestionRunStatus.Failed;

        var entry = new IngestionRunEntry(
            JobName,
            startedAtUtc,
            timeProvider.GetUtcNow(),
            status,
            entries.Count,
            startedAtUtc,
            startedAtUtc.AddHours(HorizonHours),
            failures.Count > 0 ? string.Join("; ", failures) : null
        );

        await runWriter.RecordAsync(entry, ct).ConfigureAwait(false);
        return entry;
    }
}

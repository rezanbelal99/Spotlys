using Spotlys.Application.Ingestion;
using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Pricing;

/// <summary>
/// Fetches and upserts day-ahead prices for a zone over an explicit date window
/// (docs/DATA.md §6: every job is bounded, idempotent, and honest on failure). The same
/// use case serves the daily poll (a one-day window) and the one-shot backfill (a wide
/// window) -- only the window differs.
/// </summary>
public sealed class IngestDayAheadPricesUseCase(
    IDayAheadPriceSource source,
    IPriceObservationRepository repository,
    IIngestionRunWriter runWriter,
    TimeProvider timeProvider)
{
    /// <summary>The job_name recorded in ingestion_run for every run of this use case.</summary>
    public const string JobName = "IngestDayAheadPrices";

    /// <summary>Fetches and upserts prices for <paramref name="zone"/> over
    /// [<paramref name="fromDate"/>, <paramref name="toDate"/>], inclusive.</summary>
    public async Task<IngestionRunEntry> RunAsync(
        PriceArea zone, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var startedAtUtc = timeProvider.GetUtcNow();
        var observations = new List<PriceObservation>();
        var failures = new List<string>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var result = await source.FetchDayAsync(zone, date, ct).ConfigureAwait(false);
            if (result.Status == DayAheadFetchStatus.Found)
            {
                observations.AddRange(result.Observations);
            }
            else if (result.Status == DayAheadFetchStatus.Failed)
            {
                failures.Add($"{date:yyyy-MM-dd}: {result.Error}");
            }
            // NotYetPublished: neither data nor an error. Nothing to record per-day; the
            // caller (the scheduled job) decides whether absence is expected right now.
        }

        // Never let one bad day withhold the good ones (docs/DATA.md §6: honest on failure,
        // never silently padded -- but also never silently dropped).
        if (observations.Count > 0)
        {
            await repository.UpsertRangeAsync(observations, ct).ConfigureAwait(false);
        }

        var status = failures.Count == 0
            ? IngestionRunStatus.Ok
            : observations.Count > 0
                ? IngestionRunStatus.Partial
                : IngestionRunStatus.Failed;

        var entry = new IngestionRunEntry(
            JobName,
            startedAtUtc,
            timeProvider.GetUtcNow(),
            status,
            observations.Count,
            new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(toDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            failures.Count > 0 ? string.Join("; ", failures) : null
        );

        await runWriter.RecordAsync(entry, ct).ConfigureAwait(false);
        return entry;
    }
}

using Spotlys.Application.Ingestion;

namespace Spotlys.Application.Hydrology;

/// <summary>
/// Fetches and upserts the latest week's reservoir readings for every zone, plus a
/// wholesale refresh of the min/median/max reference table (docs/DATA.md §6, §3). Mirrors
/// <c>IngestDayAheadPricesUseCase</c>'s shape.
/// </summary>
public sealed class IngestHydrologyUseCase(
    INveHydrologySource source,
    IHydrologyRepository repository,
    IIngestionRunWriter runWriter,
    TimeProvider timeProvider)
{
    /// <summary>The job_name recorded in ingestion_run for every run of this use case.</summary>
    public const string JobName = "IngestHydrology";

    /// <summary>Fetches and upserts the latest week's reservoir readings for every zone,
    /// plus a wholesale refresh of the min/median/max reference table.</summary>
    public async Task<IngestionRunEntry> RunAsync(CancellationToken ct)
    {
        var startedAtUtc = timeProvider.GetUtcNow();
        var failures = new List<string>();
        var rowCount = 0;

        var latestWeek = await source.FetchLatestWeekAsync(ct).ConfigureAwait(false);
        if (latestWeek.Status == HydrologyFetchStatus.Found)
        {
            await repository.UpsertObservationsAsync(latestWeek.Entries, ct).ConfigureAwait(false);
            rowCount += latestWeek.Entries.Count;
        }
        else
        {
            failures.Add($"latest-week: {latestWeek.Error}");
        }

        var weekNorms = await source.FetchWeekNormsAsync(ct).ConfigureAwait(false);
        if (weekNorms.Status == HydrologyFetchStatus.Found)
        {
            await repository.ReplaceWeekNormsAsync(weekNorms.Entries, ct).ConfigureAwait(false);
            rowCount += weekNorms.Entries.Count;
        }
        else
        {
            failures.Add($"week-norms: {weekNorms.Error}");
        }

        var status = failures.Count == 0
            ? IngestionRunStatus.Ok
            : rowCount > 0
                ? IngestionRunStatus.Partial
                : IngestionRunStatus.Failed;

        var entry = new IngestionRunEntry(
            JobName,
            startedAtUtc,
            timeProvider.GetUtcNow(),
            status,
            rowCount,
            null,
            null,
            failures.Count > 0 ? string.Join("; ", failures) : null
        );

        await runWriter.RecordAsync(entry, ct).ConfigureAwait(false);
        return entry;
    }
}

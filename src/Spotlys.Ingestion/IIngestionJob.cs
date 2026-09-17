using Spotlys.Application.Ingestion;

namespace Spotlys.Ingestion;

// The shape every ingestion job implements (docs/DATA.md §6): named, bounded to an
// explicit window internally, and honest about partial failure via the IngestionRunEntry
// it returns (or null if there was nothing to do). Internal: Spotlys.Ingestion is a host,
// nothing outside this assembly needs this contract (CA1515).
internal interface IIngestionJob
{
    // Matches the job_name written to ingestion_run.
    public string Name { get; }

    // How stale this feed may get before it's an incident, not a status line.
    public TimeSpan MaxStaleness { get; }

    // Runs the job as of a given instant. Returns null if the job determined there was
    // nothing to do (e.g. today's data already fetched).
    public Task<IngestionRunEntry?> RunAsync(DateTimeOffset asOf, CancellationToken ct);
}

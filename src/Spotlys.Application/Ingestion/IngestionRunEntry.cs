namespace Spotlys.Application.Ingestion;

/// <summary>Outcome of one ingestion job run (docs/DATA.md §6).</summary>
public enum IngestionRunStatus
{
    /// <summary>Every requested day fetched without error.</summary>
    Ok,

    /// <summary>At least one day failed, but some data was still upserted.</summary>
    Partial,

    /// <summary>No data was upserted; every day in the window failed.</summary>
    Failed,
}

/// <summary>
/// One row of the <c>ingestion_run</c> table -- the freshness ledger every ingestion job
/// writes to, and the only source both <c>/api/v1/status</c> and internal alerting read
/// from (docs/DEVOPS.md §7: "the dashboard can't disagree with the app").
/// </summary>
public sealed record IngestionRunEntry(
    string JobName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    IngestionRunStatus Status,
    int? Rows,
    DateTimeOffset? WindowFromUtc,
    DateTimeOffset? WindowToUtc,
    string? Error
);

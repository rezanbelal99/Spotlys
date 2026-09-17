namespace Spotlys.Application.Ingestion;

/// <summary>Read side of the ingestion_run ledger, used by <c>GET /api/v1/status</c>.</summary>
public interface IIngestionRunReader
{
    /// <summary>The most recent run for a job, or null if it has never run.</summary>
    public Task<IngestionRunEntry?> GetLatestAsync(string jobName, CancellationToken ct);
}

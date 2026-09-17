namespace Spotlys.Application.Ingestion;

/// <summary>Write side of the ingestion_run ledger. Every ingestion job calls this exactly
/// once per run, success or failure (docs/DATA.md §6).</summary>
public interface IIngestionRunWriter
{
    /// <summary>Writes one completed run to the ledger.</summary>
    public Task RecordAsync(IngestionRunEntry entry, CancellationToken ct);
}

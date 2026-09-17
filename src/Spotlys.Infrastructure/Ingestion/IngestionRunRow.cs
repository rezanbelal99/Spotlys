namespace Spotlys.Infrastructure.Ingestion;

// EF entity for ingestion_run. Carries a surrogate Id the DATA.md §6 DDL doesn't show
// (that DDL has no primary key at all) -- EF Core needs one to track rows, and it's a
// harmless addition that doesn't change any documented column. Internal: this is a mapping
// detail, never exposed outside Infrastructure (Application sees IngestionRunEntry).
internal sealed class IngestionRunRow
{
    public long Id { get; set; }
    public required string JobName { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public required string Status { get; set; }
    public int? Rows { get; set; }
    public DateTimeOffset? WindowFrom { get; set; }
    public DateTimeOffset? WindowTo { get; set; }
    public string? Error { get; set; }
}

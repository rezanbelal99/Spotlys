namespace Spotlys.Infrastructure.Metering;

/// <summary>Audit trail for the retention job (docs/DEVOPS.md §7: a purge with zero record
/// of what it did is unauditable). Not a foreign key to meter_profile -- the meter may
/// since have been deleted by the time anyone reads this row.</summary>
internal sealed class ConsumptionRetentionPurgeRow
{
    public long Id { get; set; }
    public required Guid MeterProfileId { get; set; }
    public required DateTimeOffset PurgedThroughUtc { get; set; }
    public required int RowsDeleted { get; set; }
    public required DateTimeOffset RunAtUtc { get; set; }
}

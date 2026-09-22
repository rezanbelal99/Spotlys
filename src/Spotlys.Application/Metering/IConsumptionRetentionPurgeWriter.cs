namespace Spotlys.Application.Metering;

/// <summary>Writes one audit row per meter the retention job actually purged rows for
/// (docs/DEVOPS.md §7: "a purge with zero record of what it did is unauditable").</summary>
public interface IConsumptionRetentionPurgeWriter
{
    /// <summary>Appends one audit row for a purge run against one meter.</summary>
    public Task RecordAsync(Guid meterProfileId, DateTimeOffset purgedThroughUtc, int rowsDeleted, DateTimeOffset runAtUtc, CancellationToken ct);
}

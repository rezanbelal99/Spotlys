namespace Spotlys.Application.Hydrology;

/// <summary>Persistence port for both hydrology tables. Implementations upsert on each
/// table's natural key -- idempotent (docs/DATA.md §6).</summary>
public interface IHydrologyRepository
{
    /// <summary>Upserts weekly reservoir readings on (zone, week_start_date, source).</summary>
    public Task UpsertObservationsAsync(IReadOnlyList<HydrologyObservationEntry> entries, CancellationToken ct);

    /// <summary>Replaces the min/median/max reference table wholesale (it's small and
    /// derived from NVE's full history, not an incremental time series).</summary>
    public Task ReplaceWeekNormsAsync(IReadOnlyList<HydrologyWeekNormEntry> entries, CancellationToken ct);

    /// <summary>
    /// The freshest reservoir reading for <paramref name="zone"/>/<paramref name="weekStartDate"/>
    /// that was already known at <paramref name="asOfUtc"/> -- i.e. the row with the latest
    /// <c>fetched_at_utc</c> among those with <c>fetched_at_utc &lt;= asOfUtc</c>. Never
    /// returns a row fetched after <paramref name="asOfUtc"/> (docs/FORECASTING.md §5).
    /// </summary>
    public Task<HydrologyObservationEntry?> GetObservationAsOfAsync(
        string zone, DateOnly weekStartDate, DateTimeOffset asOfUtc, CancellationToken ct);
}

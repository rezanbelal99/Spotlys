using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Metering;

/// <summary>Persistence port for docs/DATA.md §4's per-meter consumption series. Upserts on
/// the natural key (meter_profile_id, hour_start_utc) -- re-importing the same window must
/// never duplicate rows, matching the ingestion contract's idempotency rule (docs/DATA.md
/// §6) even though this is a user-triggered import, not an automated job.</summary>
public interface IConsumptionReadingRepository
{
    /// <summary>Upserts every reading on its natural key. Empty input is a no-op.</summary>
    public Task UpsertRangeAsync(Guid meterProfileId, IReadOnlyList<HourlyConsumption> readings, string source, CancellationToken ct);

    /// <summary>Realised consumption for one meter in [fromUtc, toUtc). Missing hours are
    /// simply absent -- callers must not assume a contiguous series.</summary>
    public Task<IReadOnlyList<HourlyConsumption>> GetRangeAsync(
        Guid meterProfileId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);

    /// <summary>Every reading ever imported for one meter, unbounded -- the GDPR export
    /// endpoint's source (docs/ARCHITECTURE.md §9), not something the day-to-day product
    /// screens should call.</summary>
    public Task<IReadOnlyList<HourlyConsumption>> GetAllAsync(Guid meterProfileId, CancellationToken ct);

    /// <summary>Deletes every reading strictly older than <paramref name="cutoffUtc"/> for
    /// one meter. Returns the number of rows deleted -- the retention job's own audit trail
    /// needs this (docs/DEVOPS.md §7).</summary>
    public Task<int> DeleteOlderThanAsync(Guid meterProfileId, DateTimeOffset cutoffUtc, CancellationToken ct);
}

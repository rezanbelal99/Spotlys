using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Pricing;

/// <summary>
/// Persistence port for day-ahead price observations. Implementations must upsert on the
/// natural key (zone, hour_start_utc, source) -- re-running an ingestion window must never
/// duplicate rows (docs/DATA.md §6, idempotent).
/// </summary>
public interface IPriceObservationRepository
{
    /// <summary>Upserts every observation on its natural key. Empty input is a no-op.</summary>
    public Task UpsertRangeAsync(IReadOnlyList<PriceObservation> observations, CancellationToken ct);

    /// <summary>Realised prices for a zone in [fromUtc, toUtc). Missing hours are simply
    /// absent from the result -- callers must not assume a contiguous series.</summary>
    public Task<IReadOnlyList<PriceObservation>> GetRangeAsync(
        PriceArea zone, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}

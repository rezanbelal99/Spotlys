using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Pricing;

/// <summary>Outcome of fetching one day's prices for one zone.</summary>
public enum DayAheadFetchStatus
{
    /// <summary>Prices were found and parsed.</summary>
    Found,

    /// <summary>No data for this day -- either it hasn't been published yet (today's poll
    /// for tomorrow, before ~13:00 CET) or it's a genuine historical gap (a 404 during
    /// backfill). Both are normal, not errors (docs/DATA.md §1/§6) -- the caller decides,
    /// from context, whether absence is expected right now.</summary>
    NotYetPublished,

    /// <summary>A real transport or parse failure -- the kind that should show up in
    /// <c>ingestion_run</c> as an error and can trigger an alert.</summary>
    Failed,
}

/// <summary>One day's fetch outcome. Use the static factories rather than the constructor.</summary>
public sealed record DayAheadFetchResult(
    DayAheadFetchStatus Status,
    IReadOnlyList<PriceObservation> Observations,
    string? Error)
{
    /// <summary>Prices were found and parsed for the day.</summary>
    public static DayAheadFetchResult Found(IReadOnlyList<PriceObservation> observations) =>
        new(DayAheadFetchStatus.Found, observations, null);

    /// <summary>No data for the day -- not yet published, or a genuine historical gap.</summary>
    public static DayAheadFetchResult NotYetPublished() =>
        new(DayAheadFetchStatus.NotYetPublished, [], null);

    /// <summary>A real transport or parse failure occurred fetching the day.</summary>
    public static DayAheadFetchResult Failed(string error) =>
        new(DayAheadFetchStatus.Failed, [], error);
}

/// <summary>
/// Port for the upstream day-ahead price feed (hvakosterstrommen.no, docs/DATA.md §1).
/// Implementations own rate-limiting, retry, and the required attribution/User-Agent.
/// </summary>
public interface IDayAheadPriceSource
{
    /// <summary>Fetches one zone's prices for one calendar day.</summary>
    public Task<DayAheadFetchResult> FetchDayAsync(PriceArea zone, DateOnly forDate, CancellationToken ct);
}

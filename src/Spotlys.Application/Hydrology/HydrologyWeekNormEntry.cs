namespace Spotlys.Application.Hydrology;

/// <summary>
/// One zone's historical min/median/max reservoir fill fraction for one ISO week, across
/// NVE's full published history -- the reference distribution "deviation from the 20-year
/// median" (docs/FORECASTING.md §4) is computed against. A small, wholesale-refreshed
/// lookup table, not a time series.
/// </summary>
public sealed record HydrologyWeekNormEntry(
    string Zone,
    int IsoWeek,
    float MinFillFraction,
    float MedianFillFraction,
    float MaxFillFraction,
    DateTimeOffset FetchedAtUtc
);

namespace Spotlys.Application.Hydrology;

/// <summary>
/// One price zone's weekly reservoir reading from NVE's magasinstatistikk
/// (docs/DATA.md §3). <see cref="WeekStartDate"/> is NVE's own <c>dato_Id</c> -- the Sunday
/// the reading covers -- kept distinct from <see cref="FetchedAtUtc"/> (when Spotlys
/// retrieved it) per the point-in-time discipline (docs/FORECASTING.md §5).
/// </summary>
public sealed record HydrologyObservationEntry(
    string Zone,
    DateOnly WeekStartDate,
    DateTimeOffset FetchedAtUtc,
    float FillFraction,
    float CapacityTwh,
    float FillTwh,
    DateTimeOffset? NextPublicationUtc,
    string Source
);

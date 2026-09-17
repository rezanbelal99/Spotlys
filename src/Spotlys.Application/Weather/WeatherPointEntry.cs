namespace Spotlys.Application.Weather;

/// <summary>One of the representative sample points a zone's weather is aggregated from
/// (docs/DATA.md §2: population-weighted demand point, coastal/wind point, hydrological
/// catchment point). Seeded data, not hard-coded in the ingestion job -- the set of points
/// can grow without a code change.</summary>
public sealed record WeatherPointEntry(
    string PointId,
    string Zone,
    string Kind,
    string Name,
    decimal Lat,
    decimal Lon,
    int AltitudeM
);

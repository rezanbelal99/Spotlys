namespace Spotlys.Application.Weather;

/// <summary>
/// One point's forecast for one hour, as issued in one MET model run -- the exact
/// three-timestamp shape docs/DATA.md §2 requires. <see cref="IssuedAtUtc"/> is MET's own
/// model-run time (<c>properties.meta.updated_at</c> in the Locationforecast response, not
/// the HTTP <c>Last-Modified</c> header -- confirmed live to differ from it during Phase 3
/// planning). <see cref="FetchedAtUtc"/> is when Spotlys retrieved it.
/// <see cref="ValidAtUtc"/> is the hour the forecast describes.
/// </summary>
public sealed record WeatherForecastEntry(
    string PointId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset FetchedAtUtc,
    DateTimeOffset ValidAtUtc,
    float? TempC,
    float? WindMs,
    float? WindDirDeg,
    float? CloudFrac,
    float? PrecipMm
);

namespace Spotlys.Application.Weather;

/// <summary>Outcome of fetching one point's forecast.</summary>
public enum WeatherFetchStatus
{
    /// <summary>A fresh forecast was retrieved and parsed.</summary>
    Found,

    /// <summary>Still within the cache window, or MET returned 304 -- nothing new to
    /// upsert. Not an error (docs/DATA.md §2).</summary>
    NotModified,

    /// <summary>A real transport or parse failure.</summary>
    Failed,
}

/// <summary>One point's fetch outcome. Use the static factories rather than the constructor.</summary>
public sealed record WeatherFetchResult(
    WeatherFetchStatus Status,
    IReadOnlyList<WeatherForecastEntry> Entries,
    string? Error)
{
    /// <summary>A fresh forecast was retrieved and parsed.</summary>
    public static WeatherFetchResult Found(IReadOnlyList<WeatherForecastEntry> entries) =>
        new(WeatherFetchStatus.Found, entries, null);

    /// <summary>Nothing new -- still cached or a 304.</summary>
    public static WeatherFetchResult NotModified() => new(WeatherFetchStatus.NotModified, [], null);

    /// <summary>A real transport or parse failure occurred.</summary>
    public static WeatherFetchResult Failed(string error) => new(WeatherFetchStatus.Failed, [], error);
}

/// <summary>
/// Port for the upstream weather forecast feed (MET Locationforecast 2.0,
/// docs/DATA.md §2). The implementation owns the required User-Agent and honours cache
/// headers via <see cref="IWeatherFetchCacheStore"/> itself -- callers just ask for a point.
/// </summary>
public interface IWeatherForecastSource
{
    /// <summary>Fetches the current forecast for one point, bounded to the next
    /// <paramref name="horizonHours"/> hours (docs/DATA.md §6: every job has an explicit
    /// window).</summary>
    public Task<WeatherFetchResult> FetchAsync(WeatherPointEntry point, int horizonHours, CancellationToken ct);
}

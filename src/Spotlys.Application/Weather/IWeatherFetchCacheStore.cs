namespace Spotlys.Application.Weather;

/// <summary>One point's cache state from its last successful MET fetch. Both
/// <see cref="LastModified"/> and <see cref="ExpiresAtUtc"/> come straight from
/// <c>HttpContentHeaders</c>'s own typed <c>DateTimeOffset?</c> properties -- .NET models
/// both as content headers, not response headers, so no manual string parsing happens
/// anywhere in this path.</summary>
public sealed record WeatherFetchCacheEntry(
    string PointId,
    DateTimeOffset? LastModified,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset FetchedAtUtc
);

/// <summary>
/// Persists per-point HTTP cache state so a poll inside MET's <c>Expires</c> window makes
/// zero network calls (docs/DATA.md §2: "never re-request unexpired data"). Deliberately
/// separate from <see cref="IWeatherForecastRepository"/> -- this is an HTTP-caching detail
/// the weather source needs, not something the feature builder or any other consumer of
/// weather data should have to know exists.
/// </summary>
public interface IWeatherFetchCacheStore
{
    /// <summary>The cache state for a point, or null if it has never been fetched.</summary>
    public Task<WeatherFetchCacheEntry?> GetAsync(string pointId, CancellationToken ct);

    /// <summary>Upserts a point's cache state after a fetch.</summary>
    public Task SetAsync(WeatherFetchCacheEntry entry, CancellationToken ct);
}

namespace Spotlys.Application.Weather;

/// <summary>Persistence port for weather_forecast rows. Implementations upsert on the
/// natural key (point_id, issued_at_utc, valid_at_utc) -- idempotent (docs/DATA.md §6).</summary>
public interface IWeatherForecastRepository
{
    /// <summary>Upserts every entry on its natural key. Empty input is a no-op.</summary>
    public Task UpsertRangeAsync(IReadOnlyList<WeatherForecastEntry> entries, CancellationToken ct);

    /// <summary>
    /// The freshest forecast for <paramref name="pointId"/>/<paramref name="validAtUtc"/>
    /// that was already known at <paramref name="asOfUtc"/> -- i.e. the row with the latest
    /// <c>issued_at_utc</c> among those with <c>issued_at_utc &lt;= asOfUtc</c>. Never
    /// returns a row issued after <paramref name="asOfUtc"/> (docs/FORECASTING.md §5): this
    /// is the point-in-time-safe read the feature builder and, later, live serving depend on.
    /// </summary>
    public Task<WeatherForecastEntry?> GetAsOfAsync(
        string pointId, DateTimeOffset validAtUtc, DateTimeOffset asOfUtc, CancellationToken ct);
}

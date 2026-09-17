using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Spotlys.Application.Weather;

namespace Spotlys.Infrastructure.Weather;

// MET Locationforecast 2.0's response shape, confirmed live during Phase 3 planning
// (docs/DATA.md §2). `instant.details` covers the full requested horizon; `next_1_hours`
// (hourly precipitation) only exists for roughly the first ~3.5 days -- beyond that MET
// only publishes 6h/12h buckets, which this source does not attempt to convert into an
// hourly figure (that would invent precision that isn't there). Missing precip_mm beyond
// the near horizon is therefore expected, not a bug.
internal sealed record MetResponse(
    [property: JsonPropertyName("properties")] MetProperties Properties);

internal sealed record MetProperties(
    [property: JsonPropertyName("meta")] MetMeta Meta,
    [property: JsonPropertyName("timeseries")] List<MetTimeseriesEntry> Timeseries);

// The model-run timestamp -> issued_at_utc. Confirmed live to differ from the HTTP
// Last-Modified header, which is why this is read from the body, not the response headers.
internal sealed record MetMeta(
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

internal sealed record MetTimeseriesEntry(
    [property: JsonPropertyName("time")] DateTimeOffset Time,
    [property: JsonPropertyName("data")] MetTimeseriesData Data);

internal sealed record MetTimeseriesData(
    [property: JsonPropertyName("instant")] MetInstant Instant,
    [property: JsonPropertyName("next_1_hours")] MetNextHours? Next1Hours);

internal sealed record MetInstant(
    [property: JsonPropertyName("details")] MetInstantDetails Details);

internal sealed record MetInstantDetails(
    [property: JsonPropertyName("air_temperature")] float? AirTemperature,
    [property: JsonPropertyName("wind_speed")] float? WindSpeed,
    [property: JsonPropertyName("wind_from_direction")] float? WindFromDirection,
    [property: JsonPropertyName("cloud_area_fraction")] float? CloudAreaFraction);

internal sealed record MetNextHours(
    [property: JsonPropertyName("details")] MetNextHoursDetails Details);

internal sealed record MetNextHoursDetails(
    [property: JsonPropertyName("precipitation_amount")] float? PrecipitationAmount);

/// <summary>
/// Named HttpClient (registered in InfrastructureServiceCollectionExtensions with the
/// required User-Agent, docs/DATA.md §2) honouring MET's cache headers via
/// <see cref="IWeatherFetchCacheStore"/> -- a poll inside the `Expires` window makes no
/// network call at all.
/// </summary>
internal sealed class MetLocationforecastSource(
    HttpClient httpClient,
    IWeatherFetchCacheStore cacheStore,
    TimeProvider timeProvider) : IWeatherForecastSource
{
    public async Task<WeatherFetchResult> FetchAsync(WeatherPointEntry point, int horizonHours, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var cache = await cacheStore.GetAsync(point.PointId, ct).ConfigureAwait(false);

        if (cache is { ExpiresAtUtc: not null } && cache.ExpiresAtUtc > now)
        {
            return WeatherFetchResult.NotModified();
        }

        var url = new Uri(
            $"/weatherapi/locationforecast/2.0/complete?lat={point.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + $"&lon={point.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&altitude={point.AltitudeM}",
            UriKind.Relative);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.IfModifiedSince = cache?.LastModified;

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return WeatherFetchResult.Failed(ex.Message);
        }

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            await SaveCacheAsync(point.PointId, response.Content.Headers, now, ct).ConfigureAwait(false);
            return WeatherFetchResult.NotModified();
        }

        if (!response.IsSuccessStatusCode)
        {
            return WeatherFetchResult.Failed($"HTTP {(int)response.StatusCode}");
        }

        MetResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<MetResponse>(ct).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return WeatherFetchResult.Failed($"parse error: {ex.Message}");
        }

        if (body is null)
        {
            return WeatherFetchResult.Failed("empty response body");
        }

        await SaveCacheAsync(point.PointId, response.Content.Headers, now, ct).ConfigureAwait(false);

        var issuedAt = body.Properties.Meta.UpdatedAt;
        var horizonEnd = now.AddHours(horizonHours);

        var entries = body.Properties.Timeseries
            .Where(e => e.Time <= horizonEnd)
            .Select(e => new WeatherForecastEntry(
                point.PointId,
                issuedAt,
                now,
                e.Time,
                e.Data.Instant.Details.AirTemperature,
                e.Data.Instant.Details.WindSpeed,
                e.Data.Instant.Details.WindFromDirection,
                // MET reports cloud_area_fraction as a 0-100 percentage; the column is
                // named cloud_frac (docs/DATA.md §2) -- normalised to 0-1 to match.
                e.Data.Instant.Details.CloudAreaFraction is { } cloudPct ? cloudPct / 100f : null,
                e.Data.Next1Hours?.Details.PrecipitationAmount))
            .ToList();

        return WeatherFetchResult.Found(entries);
    }

    // Last-Modified and Expires are content headers in .NET's HttpClient model (they
    // describe the representation, not the response transaction), not response headers --
    // HttpResponseHeaders.TryGetValues("Expires", ...) silently never matches. Both are
    // exposed here as their own typed DateTimeOffset? properties, so no string parsing.
    private async Task SaveCacheAsync(
        string pointId, HttpContentHeaders headers, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        await cacheStore.SetAsync(
            new WeatherFetchCacheEntry(pointId, headers.LastModified, headers.Expires, fetchedAt), ct)
            .ConfigureAwait(false);
    }
}

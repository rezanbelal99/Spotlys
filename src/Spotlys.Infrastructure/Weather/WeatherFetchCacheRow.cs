namespace Spotlys.Infrastructure.Weather;

internal sealed class WeatherFetchCacheRow
{
    public required string PointId { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
}

namespace Spotlys.Infrastructure.Weather;

internal sealed class WeatherForecastRow
{
    public required string PointId { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
    public DateTimeOffset ValidAtUtc { get; set; }
    public float? TempC { get; set; }
    public float? WindMs { get; set; }
    public float? WindDirDeg { get; set; }
    public float? CloudFrac { get; set; }
    public float? PrecipMm { get; set; }
}

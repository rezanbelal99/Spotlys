namespace Spotlys.Infrastructure.Weather;

internal sealed class WeatherPointRow
{
    public required string Id { get; set; }
    public required string Zone { get; set; }
    public required string Kind { get; set; }
    public required string Name { get; set; }
    public decimal Lat { get; set; }
    public decimal Lon { get; set; }
    public int AltitudeM { get; set; }
}

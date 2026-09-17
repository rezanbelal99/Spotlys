namespace Spotlys.Application.Weather;

/// <summary>Read-only access to the seeded weather sample points.</summary>
public interface IWeatherPointRepository
{
    /// <summary>Every seeded point for a zone.</summary>
    public Task<IReadOnlyList<WeatherPointEntry>> ListForZoneAsync(string zone, CancellationToken ct);
}

using Microsoft.EntityFrameworkCore;
using Spotlys.Application.Weather;

namespace Spotlys.Infrastructure.Weather;

internal sealed class WeatherPointRepository(SpotlysDbContext dbContext) : IWeatherPointRepository
{
    public async Task<IReadOnlyList<WeatherPointEntry>> ListForZoneAsync(string zone, CancellationToken ct) =>
        await dbContext.WeatherPoints
            .Where(p => p.Zone == zone)
            .OrderBy(p => p.Id)
            .Select(p => new WeatherPointEntry(p.Id, p.Zone, p.Kind, p.Name, p.Lat, p.Lon, p.AltitudeM))
            .ToListAsync(ct)
            .ConfigureAwait(false);
}

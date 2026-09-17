using Microsoft.EntityFrameworkCore;
using Spotlys.Application.Weather;

namespace Spotlys.Infrastructure.Weather;

internal sealed class WeatherFetchCacheRepository(SpotlysDbContext dbContext) : IWeatherFetchCacheStore
{
    public async Task<WeatherFetchCacheEntry?> GetAsync(string pointId, CancellationToken ct)
    {
        var row = await dbContext.WeatherFetchCache
            .FirstOrDefaultAsync(c => c.PointId == pointId, ct)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new WeatherFetchCacheEntry(row.PointId, row.LastModified, row.ExpiresAtUtc, row.FetchedAtUtc);
    }

    public async Task SetAsync(WeatherFetchCacheEntry entry, CancellationToken ct)
    {
        var row = await dbContext.WeatherFetchCache
            .FirstOrDefaultAsync(c => c.PointId == entry.PointId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            dbContext.WeatherFetchCache.Add(new WeatherFetchCacheRow
            {
                PointId = entry.PointId,
                LastModified = entry.LastModified,
                ExpiresAtUtc = entry.ExpiresAtUtc,
                FetchedAtUtc = entry.FetchedAtUtc,
            });
        }
        else
        {
            row.LastModified = entry.LastModified;
            row.ExpiresAtUtc = entry.ExpiresAtUtc;
            row.FetchedAtUtc = entry.FetchedAtUtc;
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

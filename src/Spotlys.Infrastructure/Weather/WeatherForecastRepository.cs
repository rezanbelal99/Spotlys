using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Spotlys.Application.Weather;

namespace Spotlys.Infrastructure.Weather;

// Same set-based INSERT ... ON CONFLICT approach as PriceObservationRepository -- natural
// key (point_id, issued_at_utc, valid_at_utc) makes re-running a poll idempotent
// (docs/DATA.md §6).
internal sealed class WeatherForecastRepository(SpotlysDbContext dbContext) : IWeatherForecastRepository
{
    public async Task UpsertRangeAsync(IReadOnlyList<WeatherForecastEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }

        var command = connection.CreateCommand();
        try
        {
            command.CommandText =
                """
                INSERT INTO weather_forecast
                    (point_id, issued_at_utc, fetched_at_utc, valid_at_utc, temp_c, wind_ms, wind_dir_deg, cloud_frac, precip_mm)
                SELECT * FROM UNNEST(@pointIds, @issuedAts, @fetchedAts, @validAts, @temps, @winds, @windDirs, @clouds, @precips)
                ON CONFLICT (point_id, issued_at_utc, valid_at_utc)
                DO UPDATE SET
                    fetched_at_utc = EXCLUDED.fetched_at_utc,
                    temp_c = EXCLUDED.temp_c,
                    wind_ms = EXCLUDED.wind_ms,
                    wind_dir_deg = EXCLUDED.wind_dir_deg,
                    cloud_frac = EXCLUDED.cloud_frac,
                    precip_mm = EXCLUDED.precip_mm;
                """;

            command.Parameters.Add(new NpgsqlParameter("pointIds", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = entries.Select(e => e.PointId).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("issuedAts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = entries.Select(e => e.IssuedAtUtc).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("fetchedAts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = entries.Select(e => e.FetchedAtUtc).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("validAts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = entries.Select(e => e.ValidAtUtc).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("temps", NpgsqlDbType.Array | NpgsqlDbType.Real)
            {
                Value = entries.Select(e => (object?)e.TempC ?? DBNull.Value).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("winds", NpgsqlDbType.Array | NpgsqlDbType.Real)
            {
                Value = entries.Select(e => (object?)e.WindMs ?? DBNull.Value).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("windDirs", NpgsqlDbType.Array | NpgsqlDbType.Real)
            {
                Value = entries.Select(e => (object?)e.WindDirDeg ?? DBNull.Value).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("clouds", NpgsqlDbType.Array | NpgsqlDbType.Real)
            {
                Value = entries.Select(e => (object?)e.CloudFrac ?? DBNull.Value).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("precips", NpgsqlDbType.Array | NpgsqlDbType.Real)
            {
                Value = entries.Select(e => (object?)e.PrecipMm ?? DBNull.Value).ToArray(),
            });

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
            if (wasClosed)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<WeatherForecastEntry?> GetAsOfAsync(
        string pointId, DateTimeOffset validAtUtc, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        var row = await dbContext.WeatherForecasts
            .Where(w => w.PointId == pointId && w.ValidAtUtc == validAtUtc && w.IssuedAtUtc <= asOfUtc)
            .OrderByDescending(w => w.IssuedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new WeatherForecastEntry(
            row.PointId, row.IssuedAtUtc, row.FetchedAtUtc, row.ValidAtUtc,
            row.TempC, row.WindMs, row.WindDirDeg, row.CloudFrac, row.PrecipMm);
    }
}

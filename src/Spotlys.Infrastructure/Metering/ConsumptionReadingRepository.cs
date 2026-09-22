using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Spotlys.Application.Metering;
using Spotlys.Domain.Pricing;

namespace Spotlys.Infrastructure.Metering;

// Same set-based UNNEST upsert pattern as PriceObservationRepository -- a CSV import is at
// most 8,760 rows (ConsumptionCsvParser's own cap, one year of hourly readings), and this is
// one round trip regardless of size. Natural key (meter_profile_id, hour_start_utc) makes
// re-importing an overlapping window idempotent (docs/DATA.md §6's ingestion contract,
// applied here even though this is a user-triggered import, not an automated job).
internal sealed class ConsumptionReadingRepository(SpotlysDbContext dbContext) : IConsumptionReadingRepository
{
    public async Task UpsertRangeAsync(Guid meterProfileId, IReadOnlyList<HourlyConsumption> readings, string source, CancellationToken ct)
    {
        if (readings.Count == 0)
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
                INSERT INTO metering.consumption_reading (meter_profile_id, hour_start_utc, kwh, source, imported_at_utc)
                SELECT * FROM UNNEST(@meterProfileIds, @hourStarts, @kwhs, @sources, @importedAts)
                ON CONFLICT (meter_profile_id, hour_start_utc)
                DO UPDATE SET
                    kwh = EXCLUDED.kwh,
                    source = EXCLUDED.source,
                    imported_at_utc = EXCLUDED.imported_at_utc;
                """;

            var now = DateTimeOffset.UtcNow;

            command.Parameters.Add(new NpgsqlParameter("meterProfileIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = readings.Select(_ => meterProfileId).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("hourStarts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = readings.Select(r => r.HourStartUtc).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("kwhs", NpgsqlDbType.Array | NpgsqlDbType.Numeric)
            {
                Value = readings.Select(r => r.Kwh).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("sources", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = readings.Select(_ => source).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("importedAts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = readings.Select(_ => now).ToArray(),
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

    public async Task<IReadOnlyList<HourlyConsumption>> GetRangeAsync(
        Guid meterProfileId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
        await dbContext.ConsumptionReadings
            .Where(c => c.MeterProfileId == meterProfileId && c.HourStartUtc >= fromUtc && c.HourStartUtc < toUtc)
            .OrderBy(c => c.HourStartUtc)
            .Select(c => new HourlyConsumption(c.HourStartUtc, c.Kwh))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<HourlyConsumption>> GetAllAsync(Guid meterProfileId, CancellationToken ct) =>
        await dbContext.ConsumptionReadings
            .Where(c => c.MeterProfileId == meterProfileId)
            .OrderBy(c => c.HourStartUtc)
            .Select(c => new HourlyConsumption(c.HourStartUtc, c.Kwh))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<int> DeleteOlderThanAsync(Guid meterProfileId, DateTimeOffset cutoffUtc, CancellationToken ct) =>
        await dbContext.ConsumptionReadings
            .Where(c => c.MeterProfileId == meterProfileId && c.HourStartUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
}

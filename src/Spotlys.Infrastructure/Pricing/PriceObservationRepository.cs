using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Infrastructure.Pricing;

// Upserts via a single set-based INSERT ... ON CONFLICT over UNNEST'd arrays rather than
// row-by-row EF tracking -- a backfill window is thousands of rows, and this is one round
// trip regardless of size. Natural key (zone, hour_start_utc, source) makes re-running an
// ingestion window idempotent (docs/DATA.md §6), which is exactly what ON CONFLICT gives.
internal sealed class PriceObservationRepository(SpotlysDbContext dbContext) : IPriceObservationRepository
{
    public async Task UpsertRangeAsync(IReadOnlyList<PriceObservation> observations, CancellationToken ct)
    {
        if (observations.Count == 0)
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
                INSERT INTO price_observation (zone, hour_start_utc, price_ex_vat, currency_rate, source, observed_at_utc)
                SELECT * FROM UNNEST(@zones, @hourStarts, @prices, @currencyRates, @sources, @observedAts)
                ON CONFLICT (zone, hour_start_utc, source)
                DO UPDATE SET
                    price_ex_vat = EXCLUDED.price_ex_vat,
                    currency_rate = EXCLUDED.currency_rate,
                    observed_at_utc = EXCLUDED.observed_at_utc;
                """;

            command.Parameters.Add(new NpgsqlParameter("zones", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = observations.Select(o => o.Zone.ToString()).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("hourStarts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = observations.Select(o => o.HourStartUtc).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("prices", NpgsqlDbType.Array | NpgsqlDbType.Numeric)
            {
                Value = observations.Select(o => o.PriceExVatOrePerKwh).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("currencyRates", NpgsqlDbType.Array | NpgsqlDbType.Numeric)
            {
                Value = observations.Select(o => (object?)o.CurrencyRate ?? DBNull.Value).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("sources", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = observations.Select(o => o.Source).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("observedAts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = observations.Select(o => o.ObservedAtUtc).ToArray(),
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

    public async Task<IReadOnlyList<PriceObservation>> GetRangeAsync(
        PriceArea zone, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
        await dbContext.PriceObservations
            .Where(p => p.Zone == zone && p.HourStartUtc >= fromUtc && p.HourStartUtc < toUtc)
            .OrderBy(p => p.HourStartUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}

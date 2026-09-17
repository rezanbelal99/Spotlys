using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Spotlys.Application.Hydrology;

namespace Spotlys.Infrastructure.Hydrology;

internal sealed class HydrologyRepository(SpotlysDbContext dbContext) : IHydrologyRepository
{
    public async Task UpsertObservationsAsync(IReadOnlyList<HydrologyObservationEntry> entries, CancellationToken ct)
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
                INSERT INTO hydrology_observation
                    (zone, week_start_date, fetched_at_utc, fill_fraction, capacity_twh, fill_twh, next_publication_utc, source)
                SELECT * FROM UNNEST(@zones, @weekStarts, @fetchedAts, @fillFractions, @capacities, @fills, @nextPubs, @sources)
                ON CONFLICT (zone, week_start_date, source)
                DO UPDATE SET
                    fetched_at_utc = EXCLUDED.fetched_at_utc,
                    fill_fraction = EXCLUDED.fill_fraction,
                    capacity_twh = EXCLUDED.capacity_twh,
                    fill_twh = EXCLUDED.fill_twh,
                    next_publication_utc = EXCLUDED.next_publication_utc;
                """;

            command.Parameters.Add(new NpgsqlParameter("zones", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = entries.Select(e => e.Zone).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("weekStarts", NpgsqlDbType.Array | NpgsqlDbType.Date)
            {
                Value = entries.Select(e => e.WeekStartDate.ToDateTime(TimeOnly.MinValue)).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("fetchedAts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = entries.Select(e => e.FetchedAtUtc).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("fillFractions", NpgsqlDbType.Array | NpgsqlDbType.Real)
            {
                Value = entries.Select(e => e.FillFraction).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("capacities", NpgsqlDbType.Array | NpgsqlDbType.Real)
            {
                Value = entries.Select(e => e.CapacityTwh).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("fills", NpgsqlDbType.Array | NpgsqlDbType.Real)
            {
                Value = entries.Select(e => e.FillTwh).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("nextPubs", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = entries.Select(e => (object?)e.NextPublicationUtc ?? DBNull.Value).ToArray(),
            });
            command.Parameters.Add(new NpgsqlParameter("sources", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = entries.Select(e => e.Source).ToArray(),
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

    public async Task ReplaceWeekNormsAsync(IReadOnlyList<HydrologyWeekNormEntry> entries, CancellationToken ct)
    {
        // Small, wholesale-refreshed reference table (docs/DATA.md §3's "min/max/median
        // across NVE's full history") -- delete-and-reinsert inside one transaction is
        // simpler and just as correct as an upsert for a table this size (~260 rows).
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await dbContext.Database.ExecuteSqlAsync($"DELETE FROM hydrology_week_norm", ct).ConfigureAwait(false);

                dbContext.HydrologyWeekNorms.AddRange(entries.Select(e => new HydrologyWeekNormRow
                {
                    Zone = e.Zone,
                    IsoWeek = e.IsoWeek,
                    MinFillFraction = e.MinFillFraction,
                    MedianFillFraction = e.MedianFillFraction,
                    MaxFillFraction = e.MaxFillFraction,
                    FetchedAtUtc = e.FetchedAtUtc,
                }));
                await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public async Task<HydrologyObservationEntry?> GetObservationAsOfAsync(
        string zone, DateOnly weekStartDate, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        var row = await dbContext.HydrologyObservations
            .Where(h => h.Zone == zone && h.WeekStartDate == weekStartDate && h.FetchedAtUtc <= asOfUtc)
            .OrderByDescending(h => h.FetchedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new HydrologyObservationEntry(
            row.Zone, row.WeekStartDate, row.FetchedAtUtc,
            row.FillFraction, row.CapacityTwh, row.FillTwh, row.NextPublicationUtc, row.Source);
    }
}

using Spotlys.Application.Metering;

namespace Spotlys.Infrastructure.Metering;

internal sealed class ConsumptionRetentionPurgeRepository(SpotlysDbContext dbContext) : IConsumptionRetentionPurgeWriter
{
    public async Task RecordAsync(
        Guid meterProfileId, DateTimeOffset purgedThroughUtc, int rowsDeleted, DateTimeOffset runAtUtc, CancellationToken ct)
    {
        dbContext.ConsumptionRetentionPurges.Add(new ConsumptionRetentionPurgeRow
        {
            MeterProfileId = meterProfileId,
            PurgedThroughUtc = purgedThroughUtc,
            RowsDeleted = rowsDeleted,
            RunAtUtc = runAtUtc,
        });

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

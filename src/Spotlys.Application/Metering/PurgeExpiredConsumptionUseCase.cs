using Spotlys.Application.Accounts;
using Spotlys.Application.Ingestion;

namespace Spotlys.Application.Metering;

/// <summary>
/// docs/ARCHITECTURE.md §5's <c>RecomputeMonthlyPeaks</c>-adjacent retention sweep (DATA.md
/// §4: "default 3 years, user-set extension"). For every meter, deletes consumption older
/// than its own <see cref="MeterProfileEntry.ConsumptionRetentionYears"/> and writes an
/// audit row for every meter it actually purged rows from (docs/DEVOPS.md §7).
/// </summary>
public sealed class PurgeExpiredConsumptionUseCase(
    IMeterProfileRepository meters,
    IConsumptionReadingRepository consumption,
    IConsumptionRetentionPurgeWriter purgeWriter,
    IIngestionRunWriter runWriter,
    TimeProvider timeProvider)
{
    /// <summary>The job_name recorded in ingestion_run for every run of this use case.</summary>
    public const string JobName = "PurgeExpiredConsumption";

    /// <summary>Purges every meter's expired consumption in one sweep and writes the
    /// ingestion_run row.</summary>
    public async Task<IngestionRunEntry> RunAsync(CancellationToken ct)
    {
        var startedAtUtc = timeProvider.GetUtcNow();
        var allMeters = await meters.ListAllAsync(ct).ConfigureAwait(false);

        var totalRowsDeleted = 0;
        foreach (var meter in allMeters)
        {
            var cutoffUtc = startedAtUtc.AddYears(-meter.ConsumptionRetentionYears);
            var rowsDeleted = await consumption.DeleteOlderThanAsync(meter.Id, cutoffUtc, ct).ConfigureAwait(false);

            if (rowsDeleted > 0)
            {
                await purgeWriter.RecordAsync(meter.Id, cutoffUtc, rowsDeleted, startedAtUtc, ct).ConfigureAwait(false);
                totalRowsDeleted += rowsDeleted;
            }
        }

        var entry = new IngestionRunEntry(
            JobName, startedAtUtc, timeProvider.GetUtcNow(), IngestionRunStatus.Ok, totalRowsDeleted, null, null, null);

        await runWriter.RecordAsync(entry, ct).ConfigureAwait(false);
        return entry;
    }
}

using Spotlys.Application.Ingestion;
using Spotlys.Application.Metering;

namespace Spotlys.Ingestion.Metering;

/// <summary>docs/ARCHITECTURE.md §5: hourly, matching RecomputeMonthlyPeaks' own cadence.
/// Over-provisioned for CSV-only import (nothing changes between runs unless a CSV was just
/// uploaded), but ready for Phase 6's live HAN feed without an interface change -- not a
/// placeholder, just currently mostly idle.</summary>
internal sealed class PurgeExpiredConsumptionJob(PurgeExpiredConsumptionUseCase useCase) : IIngestionJob
{
    public string Name => PurgeExpiredConsumptionUseCase.JobName;

    /// <summary>A missed run or two is not an incident -- retention is measured in years,
    /// not hours. Generous on purpose.</summary>
    public TimeSpan MaxStaleness { get; } = TimeSpan.FromDays(7);

    public async Task<IngestionRunEntry?> RunAsync(DateTimeOffset asOf, CancellationToken ct) =>
        await useCase.RunAsync(ct).ConfigureAwait(false);
}

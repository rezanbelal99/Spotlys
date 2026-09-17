using Spotlys.Application.Hydrology;
using Spotlys.Application.Ingestion;

namespace Spotlys.Ingestion.Hydrology;

/// <summary>docs/ARCHITECTURE.md §5: fires Wednesdays 09:00 Europe/Oslo, matching NVE's
/// own weekly publication cadence (the schedule itself lives in Program.cs).</summary>
internal sealed class IngestHydrologyJob(IngestHydrologyUseCase useCase) : IIngestionJob
{
    public string Name => IngestHydrologyUseCase.JobName;

    /// <summary>Weekly publication plus a day of slack before this is an incident.</summary>
    public TimeSpan MaxStaleness { get; } = TimeSpan.FromDays(8);

    public async Task<IngestionRunEntry?> RunAsync(DateTimeOffset asOf, CancellationToken ct) =>
        await useCase.RunAsync(ct).ConfigureAwait(false);
}

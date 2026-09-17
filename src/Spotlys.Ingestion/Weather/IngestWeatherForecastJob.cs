using Spotlys.Application.Ingestion;
using Spotlys.Application.Weather;

namespace Spotlys.Ingestion.Weather;

/// <summary>
/// docs/ARCHITECTURE.md §5: fires 04:00, 10:00, 16:00, 22:00 Europe/Oslo (the schedule
/// itself lives in Program.cs). Each firing is cheap when nothing has changed: the source
/// itself skips the network call for any point still inside MET's cache window
/// (docs/DATA.md §2), so a run where every point is fresh upserts nothing.
/// </summary>
internal sealed class IngestWeatherForecastJob(IngestWeatherForecastUseCase useCase) : IIngestionJob
{
    private const string Zone = "NO2"; // Phase 3 scope: one zone (docs/ROADMAP.md)

    public string Name => IngestWeatherForecastUseCase.JobName;

    /// <summary>Runs every 6h; a missed run or two still leaves the last forecast usable,
    /// so this is looser than the price feed's SLO.</summary>
    public TimeSpan MaxStaleness { get; } = TimeSpan.FromHours(8);

    public async Task<IngestionRunEntry?> RunAsync(DateTimeOffset asOf, CancellationToken ct) =>
        await useCase.RunAsync(Zone, ct).ConfigureAwait(false);
}

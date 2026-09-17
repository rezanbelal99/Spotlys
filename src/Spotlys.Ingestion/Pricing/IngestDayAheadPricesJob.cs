using Spotlys.Application.Ingestion;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Ingestion.Pricing;

/// <summary>
/// docs/ARCHITECTURE.md §5: fires 12:45 Europe/Oslo, then every 5 min until 14:30 (the
/// schedule itself lives in Program.cs's DailyTimeIntervalSchedule). Each firing is cheap
/// once tomorrow's prices are in: it checks first and does nothing further if so, which is
/// what gives "stop on success" (docs/DATA.md §6) without needing Quartz-level trigger
/// cancellation.
/// </summary>
internal sealed class IngestDayAheadPricesJob(
    IngestDayAheadPricesUseCase useCase,
    IPriceObservationRepository repository) : IIngestionJob
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private const PriceArea Zone = PriceArea.NO2; // Phase 1 scope: one zone (docs/ROADMAP.md)
    private const int HoursPerDay = 24;

    public string Name => IngestDayAheadPricesUseCase.JobName;

    /// <summary>docs/ARCHITECTURE.md §5's SLO: ingested by 14:30 Oslo, 99% of days.</summary>
    public TimeSpan MaxStaleness { get; } = TimeSpan.FromHours(25);

    public async Task<IngestionRunEntry?> RunAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        var osloNow = TimeZoneInfo.ConvertTime(asOf, Oslo);
        var tomorrow = DateOnly.FromDateTime(osloNow.Date.AddDays(1));

        if (await AlreadyHaveFullDayAsync(tomorrow, ct).ConfigureAwait(false))
        {
            return null;
        }

        return await useCase.RunAsync(Zone, tomorrow, tomorrow, ct).ConfigureAwait(false);
    }

    private async Task<bool> AlreadyHaveFullDayAsync(DateOnly date, CancellationToken ct)
    {
        // The Oslo-local calendar day, converted to UTC -- not UTC midnight itself, which
        // would be off by the DST offset (docs/ARCHITECTURE.md §3).
        var osloMidnight = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(osloMidnight, Oslo);
        var nextDayStartUtc = TimeZoneInfo.ConvertTimeToUtc(osloMidnight.AddDays(1), Oslo);

        var rows = await repository
            .GetRangeAsync(Zone, dayStartUtc, nextDayStartUtc, ct)
            .ConfigureAwait(false);
        // A DST transition day has 23 or 25 hours (docs/ARCHITECTURE.md §3); ">=" rather
        // than "==" so this never wrongly re-fetches an already-complete short day.
        return rows.Count >= HoursPerDay - 1;
    }
}

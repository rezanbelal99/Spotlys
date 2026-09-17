using CsCheck;
using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Tests.Pricing;

/// <summary>Shared test fixtures -- 2026 figures matching the real sourced values used to
/// seed scheme_parameter/grid_tariff (Phase 2 plan). Literal numbers are fine here: this is
/// test data, not application code, so CLAUDE.md rule 3 ("zero magic numbers") doesn't
/// apply -- the rule is about the engine never hard-coding a rate, not about tests being
/// unable to state one.</summary>
internal static class TariffEngineTestFixtures
{
    public static StromstotteParameters Stromstotte2026 { get; } = new(
        ThresholdExVatOrePerKwh: 77.00m,
        SupportRate: 0.90m);

    public static NorgesprisParameters NorgesprisHome2026 { get; } = new(
        RateExVatOrePerKwh: 40.00m,
        CapKwhPerMonth: 5000m);

    public static NorgesprisParameters NorgesprisCabin2026 { get; } = new(
        RateExVatOrePerKwh: 40.00m,
        CapKwhPerMonth: 1000m);

    public static LevyParameters Levies2026 { get; } = new(
        ForbruksavgiftExVatOrePerKwh: 7.13m,
        EnovaExVatOrePerKwh: 1.00m,
        VatRate: 0.25m);

    public static LevyParameters LeviesNo4_2026 { get; } = Levies2026 with { VatRate = 0m };

    /// <summary>Glitre Nett's real published rates, øre ex VAT (incl-VAT figure / 1.25),
    /// effective 2026-07-01 -- same source used for the real seed data.</summary>
    public static GridTariff GlitreNett2026 { get; } = new(
        GridCompanyId: "glitre-nett",
        EnergyDayExVatOrePerKwh: 42.16m / 1.25m,
        EnergyNightExVatOrePerKwh: 27.16m / 1.25m,
        CapacitySteps:
        [
            new CapacityStep(0m, 2m, 160.00m / 1.25m),
            new CapacityStep(2m, 5m, 232.50m / 1.25m),
            new CapacityStep(5m, 10m, 390.00m / 1.25m),
            new CapacityStep(10m, 15m, 730.00m / 1.25m),
            new CapacityStep(15m, 20m, 965.00m / 1.25m),
        ]);

    public static IReadOnlySet<DateOnly> NoHolidays { get; } = new HashSet<DateOnly>();

    /// <summary>Bounded decimal generator for property tests, built from a scaled
    /// <c>Gen.Int</c> rather than CsCheck 4.9.0's own <c>Gen.Decimal[min,max]</c> indexer --
    /// that indexer rejection-samples from decimal's full ~7.9e28 range down to the
    /// requested bounds and reliably exhausts its retry budget for any range this narrow
    /// (verified directly against the library, not a guess).</summary>
    public static Gen<decimal> DecimalRange(decimal min, decimal max, int decimalPlaces = 2)
    {
        var scale = (int)Math.Pow(10, decimalPlaces);
        var minScaled = (int)(min * scale);
        var maxScaled = (int)(max * scale);
        return Gen.Int[minScaled, maxScaled].Select(i => i / (decimal)scale);
    }

    /// <summary>Builds one calendar day's worth of hourly consumption + matching spot
    /// prices, walking real UTC hours (not local-clock hours) so DST transition days
    /// naturally produce 23 or 25 entries without special-casing.</summary>
    public static (List<HourlyConsumption> Consumption, Dictionary<DateTimeOffset, decimal> Prices) BuildDay(
        DateTimeOffset dayStartUtc, DateTimeOffset dayEndUtc, decimal kwhPerHour, decimal spotExVatOrePerKwh)
    {
        var consumption = new List<HourlyConsumption>();
        var prices = new Dictionary<DateTimeOffset, decimal>();

        for (var hour = dayStartUtc; hour < dayEndUtc; hour = hour.AddHours(1))
        {
            consumption.Add(new HourlyConsumption(hour, kwhPerHour));
            prices[hour] = spotExVatOrePerKwh;
        }

        return (consumption, prices);
    }
}

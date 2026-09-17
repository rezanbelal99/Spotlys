using Spotlys.Domain.Pricing;
using VerifyXunit;

namespace Spotlys.Domain.Tests.Pricing;

/// <summary>
/// Golden-file tests (docs/ENGINEERING.md §2: "A real invoice, digitised, asserted to the
/// øre"). NOT A REAL INVOICE -- confirmed with the user during Phase 2 planning that none
/// exists yet. These fixtures are hand-computed synthetic scenarios instead, verified by
/// inspection against docs/DOMAIN.md's formulas rather than a real bill. When a real invoice
/// arrives, it becomes a new [Fact] here (a new .verified.txt snapshot); nothing about the
/// harness itself needs to change.
/// </summary>
public class TariffEngineGoldenFileTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // A real invoice has monthly totals, not 744 hourly line items -- verifying the
    // summary (plus a handful of representative hours) keeps the snapshot the size an
    // invoice actually is and reviewable at a glance, rather than a 700+ line diff.
    private static object Summary(Bill bill) => new
    {
        bill.EnergyExVatNok,
        bill.EnergileddExVatNok,
        bill.KapasitetsleddExVatNok,
        bill.ForbruksavgiftExVatNok,
        bill.EnovaExVatNok,
        bill.SupplierFeeExVatNok,
        bill.VatRate,
        bill.TotalExVatNok,
        bill.TotalIncVatNok,
        bill.TopThreeAverageKw,
        HourlyLineCount = bill.HourlyLines.Count,
        FirstHour = bill.HourlyLines[0],
        PeakEveningHour = bill.HourlyLines.MaxBy(l => l.Kwh),
    };

    [Fact]
    public Task Synthetic_january_household_on_stromstotte()
    {
        // 31 days, a simple repeating diurnal shape (low overnight, higher evening),
        // constant spot price for a fully deterministic, hand-checkable fixture.
        var consumption = new List<HourlyConsumption>();
        var prices = new Dictionary<DateTimeOffset, decimal>();
        for (var h = 0; h < 31 * 24; h++)
        {
            var hour = Start.AddHours(h);
            var localHour = hour.Hour;
            var kwh = localHour is >= 17 and < 21 ? 3.0m : 0.8m;
            consumption.Add(new HourlyConsumption(hour, kwh));
            prices[hour] = 100m;
        }

        var bill = TariffEngine.CalculateBill(
            new StromstotteScheme(TariffEngineTestFixtures.Stromstotte2026),
            TariffEngineTestFixtures.GlitreNett2026,
            TariffEngineTestFixtures.Levies2026,
            supplierMarkupExVatOrePerKwh: 0m,
            supplierMonthlyFeeExVatNok: 39m,
            consumption,
            prices,
            TariffEngineTestFixtures.NoHolidays);

        return Verifier.Verify(Summary(bill));
    }

    [Fact]
    public Task Synthetic_january_household_on_norgespris_crossing_the_cap()
    {
        // High enough daily consumption that the household crosses the 5,000 kWh cap
        // partway through the month -- exercises DOMAIN.md §3b's within-month regime
        // switch, the product's own "best single feature".
        var consumption = new List<HourlyConsumption>();
        var prices = new Dictionary<DateTimeOffset, decimal>();
        for (var h = 0; h < 31 * 24; h++)
        {
            var hour = Start.AddHours(h);
            consumption.Add(new HourlyConsumption(hour, 8m)); // 8 kWh/h * 24h * 31d = 5952 kWh
            prices[hour] = 150m;
        }

        var bill = TariffEngine.CalculateBill(
            new NorgesprisScheme(TariffEngineTestFixtures.NorgesprisHome2026),
            TariffEngineTestFixtures.GlitreNett2026,
            TariffEngineTestFixtures.Levies2026,
            supplierMarkupExVatOrePerKwh: 0m,
            supplierMonthlyFeeExVatNok: 0m,
            consumption,
            prices,
            TariffEngineTestFixtures.NoHolidays);

        return Verifier.Verify(Summary(bill));
    }
}

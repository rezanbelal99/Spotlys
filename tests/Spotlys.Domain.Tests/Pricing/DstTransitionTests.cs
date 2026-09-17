using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Tests.Pricing;

/// <summary>docs/ARCHITECTURE.md §3: "The March day has no 02:00; the October day has two.
/// Both exist in the price feed. A test fixture for each transition date is mandatory."
/// Hours are built from real UTC instants (see BuildDay), so a correctly-implemented engine
/// just sees 23 or 25 real hours -- these tests exist to catch an engine that instead
/// assumes exactly 24 local hours per day somewhere.</summary>
public class DstTransitionTests
{
    [Fact]
    public void March_spring_forward_day_has_23_hours_and_bills_correctly()
    {
        // 2026-03-29 is Europe/Oslo's spring-forward date. Oslo midnight 03-29 (00:00 CET)
        // is 2026-03-28T23:00Z; Oslo midnight 03-30 (00:00 CEST) is 2026-03-29T22:00Z.
        var dayStartUtc = new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero);
        var dayEndUtc = new DateTimeOffset(2026, 3, 29, 22, 0, 0, TimeSpan.Zero);

        var (consumption, prices) = TariffEngineTestFixtures.BuildDay(dayStartUtc, dayEndUtc, kwhPerHour: 1m, spotExVatOrePerKwh: 100m);

        Assert.Equal(23, consumption.Count);

        var bill = TariffEngine.CalculateBill(
            new StromstotteScheme(TariffEngineTestFixtures.Stromstotte2026),
            TariffEngineTestFixtures.GlitreNett2026,
            TariffEngineTestFixtures.Levies2026,
            supplierMarkupExVatOrePerKwh: 0m,
            supplierMonthlyFeeExVatNok: 0m,
            consumption,
            prices,
            TariffEngineTestFixtures.NoHolidays);

        Assert.Equal(23, bill.HourlyLines.Count);
        // 1 kWh/hour for 23 hours = 23 kWh total -- the day's dögnmaks is 1 kW regardless
        // of hour count, so this mainly asserts the engine didn't crash or silently drop
        // an hour while converting to/across the transition.
        Assert.Equal(1m, bill.TopThreeAverageKw);
        Assert.True(bill.TotalIncVatNok > 0m);
    }

    [Fact]
    public void October_fall_back_day_has_25_hours_and_bills_correctly()
    {
        // 2026-10-25 is Europe/Oslo's fall-back date. Oslo midnight 10-25 (00:00 CEST) is
        // 2026-10-24T22:00Z; Oslo midnight 10-26 (00:00 CET) is 2026-10-25T23:00Z.
        var dayStartUtc = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero);
        var dayEndUtc = new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero);

        var (consumption, prices) = TariffEngineTestFixtures.BuildDay(dayStartUtc, dayEndUtc, kwhPerHour: 1m, spotExVatOrePerKwh: 100m);

        Assert.Equal(25, consumption.Count);

        var bill = TariffEngine.CalculateBill(
            new StromstotteScheme(TariffEngineTestFixtures.Stromstotte2026),
            TariffEngineTestFixtures.GlitreNett2026,
            TariffEngineTestFixtures.Levies2026,
            supplierMarkupExVatOrePerKwh: 0m,
            supplierMonthlyFeeExVatNok: 0m,
            consumption,
            prices,
            TariffEngineTestFixtures.NoHolidays);

        Assert.Equal(25, bill.HourlyLines.Count);
        Assert.Equal(1m, bill.TopThreeAverageKw);
        Assert.True(bill.TotalIncVatNok > 0m);
    }

    [Fact]
    public void Night_rate_boundary_is_correct_the_first_weekday_after_the_spring_forward_transition()
    {
        // 2026-03-29 (the transition date itself) is a Sunday, so every hour that day is
        // night regardless of hour-of-day (docs/DOMAIN.md §4b's weekend rule) -- testing
        // the hour-of-day boundary there would conflate the two rules. Monday 2026-03-30,
        // the first full weekday in the new (CEST, UTC+2) offset, isolates it: this is
        // exactly where an hour-of-day check computed from the wrong UTC offset would
        // silently misfire by an hour.
        var holidays = TariffEngineTestFixtures.NoHolidays;

        var localFiveAm = new DateTimeOffset(2026, 3, 30, 3, 0, 0, TimeSpan.Zero); // 05:00 CEST
        Assert.True(GridTariff.IsNight(localFiveAm, holidays));

        var localSixAm = new DateTimeOffset(2026, 3, 30, 4, 0, 0, TimeSpan.Zero); // 06:00 CEST
        Assert.False(GridTariff.IsNight(localSixAm, holidays));
    }
}

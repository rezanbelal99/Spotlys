using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Tests.Pricing;

/// <summary>docs/FORECASTING.md §3: "Negative prices exist... a unit test must cover a
/// negative-price day." Spot prices go negative several hours a year in NO2 (spike/README.md
/// found a real -34.61 øre/kWh hour in Phase 0's own backfill).</summary>
public class NegativePriceTests
{
    private static readonly DateTimeOffset Hour = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Stromstotte_gives_zero_support_on_a_negative_price_hour_not_a_negative_stotte()
    {
        var scheme = new StromstotteScheme(TariffEngineTestFixtures.Stromstotte2026);

        var rate = scheme.EnergyCostExVatOrePerKwh(
            spotExVatOrePerKwh: -34.61m,
            supplierMarkupExVatOrePerKwh: 0m,
            kwh: 1m,
            monthToDateKwhBeforeThisHour: 0m);

        // max(0, spot - threshold) with spot already negative must clamp to 0 support, not
        // go further negative and invert the sign twice.
        Assert.Equal(-34.61m, rate);
    }

    [Fact]
    public void Norgespris_under_cap_still_pays_the_flat_rate_even_when_spot_is_negative()
    {
        // Norgespris is symmetric (docs/DOMAIN.md §3b) -- you forgo negative-price hours
        // too, you don't get paid to consume.
        var scheme = new NorgesprisScheme(TariffEngineTestFixtures.NorgesprisHome2026);

        var rate = scheme.EnergyCostExVatOrePerKwh(
            spotExVatOrePerKwh: -10m,
            supplierMarkupExVatOrePerKwh: 0m,
            kwh: 1m,
            monthToDateKwhBeforeThisHour: 0m);

        Assert.Equal(40.00m, rate);
    }

    [Fact]
    public void A_negative_price_hour_produces_a_negative_energy_line_but_a_sane_total()
    {
        var consumption = new List<HourlyConsumption> { new(Hour, 10m) };
        var prices = new Dictionary<DateTimeOffset, decimal> { [Hour] = -34.61m };

        var bill = TariffEngine.CalculateBill(
            new StromstotteScheme(TariffEngineTestFixtures.Stromstotte2026),
            TariffEngineTestFixtures.GlitreNett2026,
            TariffEngineTestFixtures.Levies2026,
            supplierMarkupExVatOrePerKwh: 0m,
            supplierMonthlyFeeExVatNok: 0m,
            consumption,
            prices,
            TariffEngineTestFixtures.NoHolidays);

        var line = Assert.Single(bill.HourlyLines);
        Assert.Equal(-34.61m, line.SpotExVatOrePerKwh);
        Assert.True(line.EnergyCostExVatOre < 0m); // 10 kWh at a negative rate: a real credit
        // Energiledd, forbruksavgift and enova are unaffected by the negative spot price --
        // the total should still include them as positive amounts, keeping the bill sane.
        Assert.True(bill.EnergileddExVatNok > 0m);
        Assert.True(bill.ForbruksavgiftExVatNok > 0m);
    }
}

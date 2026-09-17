using CsCheck;
using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Tests.Pricing;

public class TariffEnginePropertyTests
{
    [Fact]
    public void Raising_spot_price_never_lowers_net_energy_cost_under_stromstotte()
    {
        var scheme = new StromstotteScheme(TariffEngineTestFixtures.Stromstotte2026);

        Gen.Select(
                TariffEngineTestFixtures.DecimalRange(0m, 300m),
                TariffEngineTestFixtures.DecimalRange(0m, 100m))
            .Sample((spot, increase) =>
            {
                var before = scheme.EnergyCostExVatOrePerKwh(spot, 0m, 1m, 0m);
                var after = scheme.EnergyCostExVatOrePerKwh(spot + increase, 0m, 1m, 0m);

                Assert.True(after >= before);
            });
    }

    [Fact]
    public void Raising_one_hours_kwh_never_lowers_the_monthly_bill_at_non_negative_prices()
    {
        // Constrained to non-negative spot prices: at a sufficiently negative price, the
        // energy line itself can go negative enough that consuming *more* in that hour
        // genuinely lowers the total (you're being paid to consume) -- correct economics,
        // not a bug, but it means this property only holds for non-negative prices. Noted
        // rather than silently narrowed.
        var gen =
            from hourCount in Gen.Int[1, 48]
            from kwhs in TariffEngineTestFixtures.DecimalRange(0m, 10m).List[hourCount, hourCount]
            from spots in TariffEngineTestFixtures.DecimalRange(0m, 300m).List[hourCount, hourCount]
            from targetHour in Gen.Int[0, hourCount - 1]
            from increase in TariffEngineTestFixtures.DecimalRange(0m, 5m)
            select (kwhs, spots, targetHour, increase);

        gen.Sample(t =>
        {
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var hours = Enumerable.Range(0, t.kwhs.Count).Select(i => start.AddHours(i)).ToList();

            var prices = hours.Zip(t.spots, (h, p) => (h, p)).ToDictionary(x => x.h, x => x.p);

            List<HourlyConsumption> BuildConsumption(decimal extraOnTarget)
            {
                return hours.Select((h, i) => new HourlyConsumption(
                        h, i == t.targetHour ? t.kwhs[i] + extraOnTarget : t.kwhs[i]))
                    .ToList();
            }

            var scheme = new StromstotteScheme(TariffEngineTestFixtures.Stromstotte2026);
            var billBefore = TariffEngine.CalculateBill(
                scheme, TariffEngineTestFixtures.GlitreNett2026, TariffEngineTestFixtures.Levies2026,
                0m, 0m, BuildConsumption(0m), prices, TariffEngineTestFixtures.NoHolidays);
            var billAfter = TariffEngine.CalculateBill(
                scheme, TariffEngineTestFixtures.GlitreNett2026, TariffEngineTestFixtures.Levies2026,
                0m, 0m, BuildConsumption(t.increase), prices, TariffEngineTestFixtures.NoHolidays);

            Assert.True(
                billAfter.TotalExVatNok >= billBefore.TotalExVatNok,
                $"total dropped from {billBefore.TotalExVatNok} to {billAfter.TotalExVatNok} " +
                $"after adding {t.increase} kWh to hour {t.targetHour}");
        });
    }

    [Fact]
    public void Norgespris_never_charges_more_than_the_flat_rate_while_under_the_cap()
    {
        var parameters = TariffEngineTestFixtures.NorgesprisHome2026;
        var scheme = new NorgesprisScheme(parameters);

        Gen.Select(
                TariffEngineTestFixtures.DecimalRange(0m, 300m),
                TariffEngineTestFixtures.DecimalRange(0m, parameters.CapKwhPerMonth - 1))
            .Sample((spot, monthToDateBefore) =>
            {
                var rate = scheme.EnergyCostExVatOrePerKwh(spot, 0m, 1m, monthToDateBefore);
                Assert.Equal(parameters.RateExVatOrePerKwh, rate);
            });
    }
}

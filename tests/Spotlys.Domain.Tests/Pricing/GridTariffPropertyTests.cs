using CsCheck;
using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Tests.Pricing;

/// <summary>Property tests for kapasitetsledd's step lookup (docs/FORECASTING.md §8's
/// property-testing style, applied to the tariff engine per the Phase 2 hard requirement:
/// "monotonicity, and behaviour exactly at capacity-step boundaries").</summary>
public class GridTariffPropertyTests
{
    [Fact]
    public void Higher_peak_kw_never_selects_a_cheaper_capacity_step()
    {
        var tariff = TariffEngineTestFixtures.GlitreNett2026;

        Gen.Select(
                TariffEngineTestFixtures.DecimalRange(0m, 25m),
                TariffEngineTestFixtures.DecimalRange(0m, 10m))
            .Sample((peakKw, increaseKw) =>
            {
                var costBefore = tariff.CapacityStepMonthlyExVatNok(peakKw);
                var costAfter = tariff.CapacityStepMonthlyExVatNok(peakKw + increaseKw);

                Assert.True(
                    costAfter >= costBefore,
                    $"peak {peakKw}+{increaseKw}={peakKw + increaseKw} kW costs {costAfter} " +
                    $"< peak {peakKw} kW's {costBefore}");
            });
    }

    [Fact]
    public void A_peak_at_exactly_a_step_boundary_lands_in_the_next_step_up()
    {
        // Half-open [from, to) convention: exactly `to` belongs to the NEXT step, not this
        // one. Generated across random, valid, non-decreasing 3-step tables rather than
        // just asserted against the one fixed Glitre Nett table.
        var stepGen =
            from b1 in TariffEngineTestFixtures.DecimalRange(1m, 10m)
            from b2 in TariffEngineTestFixtures.DecimalRange(1m, 10m)
            from b3 in TariffEngineTestFixtures.DecimalRange(1m, 10m)
            from c1 in TariffEngineTestFixtures.DecimalRange(10m, 100m)
            from c2 in TariffEngineTestFixtures.DecimalRange(1m, 100m)
            from c3 in TariffEngineTestFixtures.DecimalRange(1m, 100m)
            select (b1, gap2: b2, gap3: b3, c1, c2gain: c2, c3gain: c3);

        stepGen.Sample(t =>
        {
            var from1 = 0m;
            var to1 = t.b1;
            var to2 = to1 + t.gap2;
            var to3 = to2 + t.gap3;
            var cost1 = t.c1;
            var cost2 = cost1 + t.c2gain; // strictly increasing, a valid real step table
            var cost3 = cost2 + t.c3gain;

            var tariff = new GridTariff(
                "test",
                EnergyDayExVatOrePerKwh: 30m,
                EnergyNightExVatOrePerKwh: 20m,
                CapacitySteps:
                [
                    new CapacityStep(from1, to1, cost1),
                    new CapacityStep(to1, to2, cost2),
                    new CapacityStep(to2, to3, cost3),
                ]);

            Assert.Equal(cost2, tariff.CapacityStepMonthlyExVatNok(to1));
            Assert.Equal(cost3, tariff.CapacityStepMonthlyExVatNok(to2));
            // Just below the boundary still belongs to the lower step.
            Assert.Equal(cost1, tariff.CapacityStepMonthlyExVatNok(to1 - 0.001m));
            Assert.Equal(cost2, tariff.CapacityStepMonthlyExVatNok(to2 - 0.001m));
        });
    }

    [Fact]
    public void Top_three_average_never_exceeds_the_single_highest_daily_peak()
    {
        var consumptionGen = TariffEngineTestFixtures.DecimalRange(0m, 20m).List[1, 200]
            .Select(kwhValues => kwhValues
                .Select((kwh, i) => new HourlyConsumption(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i), kwh))
                .ToList());

        consumptionGen.Sample(consumption =>
        {
            var topThreeAverage = GridTariff.CalculateTopThreeAverageKw(consumption);
            var maxSingleHour = consumption.Count == 0 ? 0m : consumption.Max(c => c.Kwh);

            Assert.True(topThreeAverage <= maxSingleHour);
        });
    }
}

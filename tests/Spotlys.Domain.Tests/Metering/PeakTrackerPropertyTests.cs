using CsCheck;
using Spotlys.Domain.Metering;
using Spotlys.Domain.Pricing;
using Spotlys.Domain.Tests.Pricing;

namespace Spotlys.Domain.Tests.Metering;

/// <summary>Reuses <see cref="TariffEngineTestFixtures.GlitreNett2026"/> and its
/// <c>DecimalRange</c> generator -- same style as <c>GridTariffPropertyTests</c>, applied
/// to the new type rather than the existing one it wraps.</summary>
public class PeakTrackerPropertyTests
{
    [Fact]
    public void Average_never_exceeds_the_single_highest_daily_peak()
    {
        var consumptionGen = TariffEngineTestFixtures.DecimalRange(0m, 20m).List[1, 200]
            .Select(kwhValues => kwhValues
                .Select((kwh, i) => new HourlyConsumption(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i), kwh))
                .ToList());

        consumptionGen.Sample(consumption =>
        {
            var summary = PeakTracker.Summarize(consumption, TariffEngineTestFixtures.GlitreNett2026.CapacitySteps);
            var maxSingleHour = consumption.Count == 0 ? 0m : consumption.Max(c => c.Kwh);

            Assert.True(summary.AverageKw <= maxSingleHour);
        });
    }

    [Fact]
    public void Headroom_is_never_negative()
    {
        var consumptionGen = TariffEngineTestFixtures.DecimalRange(0m, 20m).List[1, 200]
            .Select(kwhValues => kwhValues
                .Select((kwh, i) => new HourlyConsumption(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i), kwh))
                .ToList());

        consumptionGen.Sample(consumption =>
        {
            var summary = PeakTracker.Summarize(consumption, TariffEngineTestFixtures.GlitreNett2026.CapacitySteps);
            Assert.True(summary.HeadroomToNextStepKw >= 0m);
        });
    }

    [Fact]
    public void Top_three_are_ranked_highest_first()
    {
        var consumptionGen = TariffEngineTestFixtures.DecimalRange(0m, 20m).List[3, 60]
            .Select(kwhValues => kwhValues
                .Select((kwh, i) => new HourlyConsumption(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i), kwh))
                .ToList());

        consumptionGen.Sample(consumption =>
        {
            var summary = PeakTracker.Summarize(consumption, TariffEngineTestFixtures.GlitreNett2026.CapacitySteps);
            for (var i = 1; i < summary.TopThree.Count; i++)
            {
                Assert.True(summary.TopThree[i - 1].PeakKw >= summary.TopThree[i].PeakKw);
            }
        });
    }
}

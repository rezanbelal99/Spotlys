using CsCheck;
using Spotlys.Domain.Pricing;
using Spotlys.Domain.Scheduling;
using Spotlys.Domain.Tests.Pricing;

namespace Spotlys.Domain.Tests.Scheduling;

/// <summary>Shared generators for the optimizer's property tests, mirroring
/// <see cref="TariffEngineTestFixtures"/>'s style (its <c>DecimalRange</c> is reused
/// directly rather than re-implemented).</summary>
internal static class LoadOptimizerTestFixtures
{
    public static readonly DateTimeOffset WindowStart = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A feasible <see cref="FlexibleLoad"/> plus the hourly marginal-cost and
    /// baseline-load dictionaries it'll be scheduled against -- generated together so the
    /// load's window always lines up with real hours in both dictionaries.</summary>
    public static Gen<(FlexibleLoad Load, Dictionary<DateTimeOffset, decimal> MarginalCost, Dictionary<DateTimeOffset, decimal> BaselineLoadKw)>
        FeasibleScenario
    { get; } =
        from windowHours in Gen.Int[4, 48]
        from maxPowerKw in TariffEngineTestFixtures.DecimalRange(1m, 11m)
            // Keep energy comfortably schedulable within the window at max power, so
            // "feasible" isn't a knife-edge the generator rarely hits.
        from energyFraction in TariffEngineTestFixtures.DecimalRange(0.1m, 0.7m)
        from minPowerKw in TariffEngineTestFixtures.DecimalRange(0m, 1m)
        from interruptible in Gen.Bool
        from costs in TariffEngineTestFixtures.DecimalRange(5m, 300m).List[windowHours, windowHours]
        from baselines in TariffEngineTestFixtures.DecimalRange(0m, 2m).List[windowHours, windowHours]
        let notBefore = WindowStart
        let deadline = WindowStart.AddHours(windowHours)
        let energyKwh = windowHours * maxPowerKw * energyFraction
        let load = new FlexibleLoad(energyKwh, maxPowerKw, Math.Min(minPowerKw, maxPowerKw), notBefore, deadline, interruptible)
        let marginalCost = Enumerable.Range(0, windowHours)
            .ToDictionary(i => notBefore.AddHours(i), i => costs[i])
        let baselineLoadKw = Enumerable.Range(0, windowHours)
            .ToDictionary(i => notBefore.AddHours(i), i => baselines[i])
        select (load, marginalCost, baselineLoadKw);

    /// <summary>A small, realistic capacity-step table (same shape as
    /// <see cref="TariffEngineTestFixtures.GlitreNett2026"/>) plus a plausible
    /// already-locked-in month-to-date peak.</summary>
    public static Gen<(IReadOnlyList<CapacityStep> Steps, decimal CurrentThirdHighestPeakKw)> CapacityScenario { get; } =
        from step1To in TariffEngineTestFixtures.DecimalRange(1m, 5m)
        from step2Gap in TariffEngineTestFixtures.DecimalRange(1m, 5m)
        from step3Gap in TariffEngineTestFixtures.DecimalRange(1m, 5m)
        from cost1 in TariffEngineTestFixtures.DecimalRange(50m, 150m)
        from cost2Gain in TariffEngineTestFixtures.DecimalRange(20m, 100m)
        from cost3Gain in TariffEngineTestFixtures.DecimalRange(20m, 100m)
        from currentPeakKw in TariffEngineTestFixtures.DecimalRange(0m, 10m)
        let step2To = step1To + step2Gap
        let step3To = step2To + step3Gap
        let steps = new List<CapacityStep>
        {
            new(0m, step1To, cost1),
            new(step1To, step2To, cost1 + cost2Gain),
            new(step2To, step3To, cost1 + cost2Gain + cost3Gain),
        }
        select ((IReadOnlyList<CapacityStep>)steps, currentPeakKw);
}

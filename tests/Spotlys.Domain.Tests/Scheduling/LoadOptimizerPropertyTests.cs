using CsCheck;
using Spotlys.Domain.Pricing;
using Spotlys.Domain.Scheduling;

namespace Spotlys.Domain.Tests.Scheduling;

/// <summary>The five invariants docs/FORECASTING.md §8 names, each a CsCheck property test
/// rather than an example test -- per the phase's own instruction. Generators only build
/// feasible scenarios (<see cref="LoadOptimizerTestFixtures.FeasibleScenario"/>), so these
/// invariants test the algorithm's real behaviour rather than its documented failure mode
/// (an infeasible load legitimately returns <c>IsFullyScheduled = false</c>).</summary>
public class LoadOptimizerPropertyTests
{
    private static readonly Gen<(FlexibleLoad Load, Dictionary<DateTimeOffset, decimal> MarginalCost,
        Dictionary<DateTimeOffset, decimal> BaselineLoadKw, IReadOnlyList<CapacityStep> Steps, decimal CurrentPeakKw)>
        FullScenario =
        LoadOptimizerTestFixtures.FeasibleScenario.SelectMany(scenario =>
            LoadOptimizerTestFixtures.CapacityScenario.Select(capacity =>
                (scenario.Load, scenario.MarginalCost, scenario.BaselineLoadKw, capacity.Steps, capacity.CurrentThirdHighestPeakKw)));

    [Fact]
    public void Total_scheduled_energy_equals_requested_energy_on_feasible_inputs()
    {
        FullScenario.Sample(s =>
        {
            var schedule = LoadOptimizer.Optimize(s.Load, s.MarginalCost, s.BaselineLoadKw, s.Steps, s.CurrentPeakKw);

            Assert.True(schedule.IsFullyScheduled, "generator only builds feasible scenarios");
            var totalAllocated = schedule.Allocations.Sum(a => a.AllocatedKwh);
            Assert.True(
                Math.Abs(totalAllocated - s.Load.EnergyKwh) < 0.001m,
                $"allocated {totalAllocated} kWh, requested {s.Load.EnergyKwh} kWh");
        });
    }

    [Fact]
    public void Never_schedules_outside_the_load_window()
    {
        FullScenario.Sample(s =>
        {
            var schedule = LoadOptimizer.Optimize(s.Load, s.MarginalCost, s.BaselineLoadKw, s.Steps, s.CurrentPeakKw);

            foreach (var allocation in schedule.Allocations)
            {
                Assert.True(allocation.HourStartUtc >= s.Load.NotBefore);
                Assert.True(allocation.HourStartUtc < s.Load.Deadline);
            }
        });
    }

    [Fact]
    public void Never_exceeds_max_power_kw_in_any_hour()
    {
        FullScenario.Sample(s =>
        {
            var schedule = LoadOptimizer.Optimize(s.Load, s.MarginalCost, s.BaselineLoadKw, s.Steps, s.CurrentPeakKw);

            foreach (var allocation in schedule.Allocations)
            {
                Assert.True(
                    allocation.AllocatedKwh <= s.Load.MaxPowerKw + 0.001m,
                    $"hour {allocation.HourStartUtc} allocated {allocation.AllocatedKwh} kWh " +
                    $"> MaxPowerKw {s.Load.MaxPowerKw}");
            }
        });
    }

    [Fact]
    public void Optimized_schedule_cost_never_exceeds_a_naive_immediate_schedule()
    {
        FullScenario.Sample(s =>
        {
            var optimized = LoadOptimizer.Optimize(s.Load, s.MarginalCost, s.BaselineLoadKw, s.Steps, s.CurrentPeakKw);
            var naive = LoadOptimizer.NaiveImmediateSchedule(s.Load, s.MarginalCost, s.BaselineLoadKw, s.Steps, s.CurrentPeakKw);

            Assert.True(
                optimized.TotalCostExVatOre <= naive.TotalCostExVatOre + 0.01m,
                $"optimized cost {optimized.TotalCostExVatOre} > naive cost {naive.TotalCostExVatOre}");
        });
    }

    [Fact]
    public void Lowering_one_hours_price_never_decreases_that_hours_allocation()
    {
        FullScenario.Sample(s =>
        {
            var before = LoadOptimizer.Optimize(s.Load, s.MarginalCost, s.BaselineLoadKw, s.Steps, s.CurrentPeakKw);
            if (s.MarginalCost.Count == 0)
            {
                return;
            }

            var targetHour = s.MarginalCost.Keys.First();
            var loweredCost = new Dictionary<DateTimeOffset, decimal>(s.MarginalCost)
            {
                [targetHour] = s.MarginalCost[targetHour] / 2m,
            };

            var after = LoadOptimizer.Optimize(s.Load, loweredCost, s.BaselineLoadKw, s.Steps, s.CurrentPeakKw);

            var allocatedBefore = before.Allocations
                .Where(a => a.HourStartUtc == targetHour).Sum(a => a.AllocatedKwh);
            var allocatedAfter = after.Allocations
                .Where(a => a.HourStartUtc == targetHour).Sum(a => a.AllocatedKwh);

            Assert.True(
                allocatedAfter >= allocatedBefore - 0.001m,
                $"hour {targetHour}: allocation dropped from {allocatedBefore} to {allocatedAfter} kWh " +
                "after its price was lowered");
        });
    }

}

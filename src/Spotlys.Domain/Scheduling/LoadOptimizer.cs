using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Scheduling;

/// <summary>
/// Schedules a <see cref="FlexibleLoad"/> against a marginal-cost-after-support forecast
/// (docs/FORECASTING.md §8). Pure: no I/O, no ambient time, no async -- every input is
/// already resolved by the caller (the Application layer turns a <c>ForecastFan</c> and a
/// <c>SupportScheme</c> into the marginal-cost dictionary; the tariff engine's
/// <see cref="GridTariff"/> supplies the capacity-step table).
///
/// <para>Method: for a fixed peak ceiling, the inner problem is a linear allocation over
/// hours -- sort by marginal cost after support and fill greedily up to
/// <c>min(MaxPowerKw, ceiling - baseload)</c>. The outer loop only tries the grid company's
/// own step boundaries (5-8 candidates), since those are the only ceilings where the
/// capacity-step cost actually changes; picking the lowest total wins. This is exactly
/// optimal for this problem's structure and, unlike a general MILP, is explainable to a
/// user in one sentence -- see docs/adr/0006-greedy-optimizer-not-milp.md.</para>
/// </summary>
public static class LoadOptimizer
{
    /// <param name="load">What needs to be scheduled and by when.</param>
    /// <param name="marginalCostExVatOrePerKwh">Expected cost after support, one entry per
    /// candidate hour (docs/FORECASTING.md §8: "don't optimise the median" -- the caller
    /// derives this from the quantile fan's expected value, not just q50). An hour missing
    /// from this dictionary is treated as unusable, not zero-cost.</param>
    /// <param name="baselineLoadKw">The household's existing (non-flexible) draw per hour;
    /// an hour missing here is treated as zero baseline.</param>
    /// <param name="capacitySteps">The meter's grid company's kapasitetsledd step table.</param>
    /// <param name="currentThirdHighestPeakKw">This month's already-locked-in third-highest
    /// daily peak (docs/DOMAIN.md §4a) -- the floor below which lowering the ceiling changes
    /// nothing, since that peak is sunk cost regardless of this schedule's choice.</param>
    public static Schedule Optimize(
        FlexibleLoad load,
        IReadOnlyDictionary<DateTimeOffset, decimal> marginalCostExVatOrePerKwh,
        IReadOnlyDictionary<DateTimeOffset, decimal> baselineLoadKw,
        IReadOnlyList<CapacityStep> capacitySteps,
        decimal currentThirdHighestPeakKw)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(marginalCostExVatOrePerKwh);
        ArgumentNullException.ThrowIfNull(baselineLoadKw);
        ArgumentNullException.ThrowIfNull(capacitySteps);

        // Candidate self-imposed ceilings -- one per capacity-step tier, each set to the
        // MAXIMUM headroom obtainable while still realizing a peak inside that tier. More
        // headroom within the same tier is always at least as good (never costs extra), so
        // a step's own FromKw is a dominated candidate -- what actually matters is "how
        // close can we get to the next tier without crossing into it." Using step.ToKw
        // itself here would be wrong: CapacityStep's own half-open [From,To) convention
        // means a *realized* peak of exactly ToKw belongs to the NEXT tier (see
        // BuildCandidate's realized-peak pricing), so this backs off by the smallest
        // meaningful increment at this domain's usual 2-decimal-place kW precision --
        // matching both real tariff tables (docs/DOMAIN.md's rates are quoted to 2
        // decimals) and this project's own generated test data.
        const decimal SmallestMeaningfulKw = 0.01m;

        var candidateCeilings = new SortedSet<decimal> { currentThirdHighestPeakKw };
        foreach (var step in capacitySteps)
        {
            var maxHeadroomInThisTier = step.ToKw - SmallestMeaningfulKw;
            candidateCeilings.Add(Math.Max(maxHeadroomInThisTier, currentThirdHighestPeakKw));
        }

        // The step table's highest tier isn't necessarily high enough to let the load draw
        // its full MaxPowerKw -- the capacity-step rate is flat above the last defined step
        // (GridTariff.CapacityStepMonthlyExVatNok's own fallback), so trying a ceiling this
        // generous never costs more than the top step's rate once realized-peak pricing is
        // applied; without it, a load whose MaxPowerKw exceeds every step's ToKw could never
        // be offered enough headroom to fully schedule.
        var highestPossibleBaselineKw = baselineLoadKw.Count > 0 ? baselineLoadKw.Values.Max() : 0m;
        candidateCeilings.Add(highestPossibleBaselineKw + load.MaxPowerKw);

        var currentCapacityCostExVatNok = GridTariff.CapacityStepMonthlyExVatNok(capacitySteps, currentThirdHighestPeakKw);

        Schedule? best = null;
        foreach (var ceiling in candidateCeilings)
        {
            var candidate = BuildCandidate(
                load, marginalCostExVatOrePerKwh, baselineLoadKw, capacitySteps,
                currentThirdHighestPeakKw, currentCapacityCostExVatNok, ceiling);
            best = Choose(best, candidate);
        }

        return best!;
    }

    private static Schedule BuildCandidate(
        FlexibleLoad load,
        IReadOnlyDictionary<DateTimeOffset, decimal> marginalCost,
        IReadOnlyDictionary<DateTimeOffset, decimal> baselineLoadKw,
        IReadOnlyList<CapacityStep> capacitySteps,
        decimal currentThirdHighestPeakKw,
        decimal currentCapacityCostExVatNok,
        decimal ceilingKw)
    {
        var (allocations, remainingKwh, energyCostExVatOre) = AllocateGreedy(load, marginalCost, baselineLoadKw, ceilingKw);

        // Price the capacity term off the peak this schedule actually *realizes*, not the
        // self-imposed ceilingKw bound used to constrain the greedy fill -- a ceiling tried
        // by the outer loop but never actually needed (the greedy fill stayed well under it)
        // must not be charged as if it were reached. CapacityStep's own half-open [From,To)
        // convention means a realized peak of exactly a step's ToKw correctly prices into
        // the next step up; that's the domain rule, just applied to the real number now.
        var realizedPeakKw = allocations.Count == 0
            ? currentThirdHighestPeakKw
            : Math.Max(
                currentThirdHighestPeakKw,
                allocations.Max(a => baselineLoadKw.GetValueOrDefault(a.HourStartUtc, 0m) + a.AllocatedKwh));

        var capacityCostExVatNok = GridTariff.CapacityStepMonthlyExVatNok(capacitySteps, realizedPeakKw);
        // The two lower peaks are sunk cost regardless of this schedule's choice -- only the
        // delta above the peak already locked in this month is attributable to this load.
        var capacityDeltaExVatNok = Math.Max(0m, capacityCostExVatNok - currentCapacityCostExVatNok);
        var totalCostExVatOre = energyCostExVatOre + capacityDeltaExVatNok * 100m; // NOK -> øre

        var isFullyScheduled = remainingKwh <= 0m;
        var bindingConstraint = DetermineBindingConstraint(
            load, marginalCost, remainingKwh, realizedPeakKw, currentThirdHighestPeakKw);

        return new Schedule(
            allocations, realizedPeakKw, totalCostExVatOre, isFullyScheduled,
            Math.Max(0m, remainingKwh), bindingConstraint);
    }

    private static (List<HourAllocation> Allocations, decimal RemainingKwh, decimal EnergyCostExVatOre) AllocateGreedy(
        FlexibleLoad load,
        IReadOnlyDictionary<DateTimeOffset, decimal> marginalCost,
        IReadOnlyDictionary<DateTimeOffset, decimal> baselineLoadKw,
        decimal ceilingKw)
    {
        var candidateHours = marginalCost.Keys
            .Where(h => h >= load.NotBefore && h < load.Deadline)
            .OrderBy(h => marginalCost[h])
            .ThenBy(h => h)
            .ToList();

        return AllocateInOrder(load, marginalCost, baselineLoadKw, ceilingKw, candidateHours);
    }

    private static (List<HourAllocation> Allocations, decimal RemainingKwh, decimal EnergyCostExVatOre) AllocateInOrder(
        FlexibleLoad load,
        IReadOnlyDictionary<DateTimeOffset, decimal> marginalCost,
        IReadOnlyDictionary<DateTimeOffset, decimal> baselineLoadKw,
        decimal ceilingKw,
        IReadOnlyList<DateTimeOffset> candidateHoursInFillOrder)
    {
        var remaining = load.EnergyKwh;
        var allocations = new List<HourAllocation>();
        var energyCostExVatOre = 0m;

        foreach (var hour in candidateHoursInFillOrder)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var baseline = baselineLoadKw.GetValueOrDefault(hour, 0m);
            var headroomKw = Math.Max(0m, ceilingKw - baseline);
            var hourCapKw = Math.Min(load.MaxPowerKw, headroomKw);

            if (hourCapKw <= 0m)
            {
                continue;
            }

            // A non-interruptible load can't meaningfully run in an hour that can't even
            // reach its minimum operating power -- skip it entirely rather than allocate a
            // token amount. (The final partial hour that finishes the job is exempt from
            // this floor -- see FlexibleLoad.MinPowerKw's own remarks.)
            if (!load.Interruptible && hourCapKw < load.MinPowerKw)
            {
                continue;
            }

            var allocatedKwh = Math.Min(remaining, hourCapKw);
            var costPerKwh = marginalCost[hour];
            allocations.Add(new HourAllocation(hour, allocatedKwh, costPerKwh));
            energyCostExVatOre += allocatedKwh * costPerKwh;
            remaining -= allocatedKwh;
        }

        return (allocations, remaining, energyCostExVatOre);
    }

    /// <summary>Fills the load starting at <see cref="FlexibleLoad.NotBefore"/> regardless
    /// of cost -- the "plug in now" baseline docs/FORECASTING.md §8's counterfactual
    /// compares against, and the naive strategy the property tests assert
    /// <see cref="Optimize"/> always beats or matches. No self-imposed ceiling beyond what
    /// the load's own <see cref="FlexibleLoad.MaxPowerKw"/> needs -- matches
    /// <see cref="Optimize"/>'s own "always try enough headroom" candidate, so this isn't
    /// artificially starved of headroom the real optimizer would have available too.</summary>
    public static Schedule NaiveImmediateSchedule(
        FlexibleLoad load,
        IReadOnlyDictionary<DateTimeOffset, decimal> marginalCostExVatOrePerKwh,
        IReadOnlyDictionary<DateTimeOffset, decimal> baselineLoadKw,
        IReadOnlyList<CapacityStep> capacitySteps,
        decimal currentThirdHighestPeakKw)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(marginalCostExVatOrePerKwh);
        ArgumentNullException.ThrowIfNull(baselineLoadKw);
        ArgumentNullException.ThrowIfNull(capacitySteps);

        var highestBaselineKw = baselineLoadKw.Count > 0 ? baselineLoadKw.Values.Max() : 0m;
        var ceiling = Math.Max(highestBaselineKw + load.MaxPowerKw, currentThirdHighestPeakKw);
        var candidateHours = marginalCostExVatOrePerKwh.Keys
            .Where(h => h >= load.NotBefore && h < load.Deadline)
            .OrderBy(h => h)
            .ToList();

        var (allocations, remainingKwh, energyCostExVatOre) =
            AllocateInOrder(load, marginalCostExVatOrePerKwh, baselineLoadKw, ceiling, candidateHours);

        // Same rule Optimize itself uses: price off the peak actually realized, not the
        // (here, deliberately generous) self-imposed ceiling.
        var realizedPeakKw = allocations.Count == 0
            ? currentThirdHighestPeakKw
            : Math.Max(currentThirdHighestPeakKw, allocations.Max(a => baselineLoadKw.GetValueOrDefault(a.HourStartUtc, 0m) + a.AllocatedKwh));

        var capacityDeltaExVatNok = Math.Max(0m,
            GridTariff.CapacityStepMonthlyExVatNok(capacitySteps, realizedPeakKw) -
            GridTariff.CapacityStepMonthlyExVatNok(capacitySteps, currentThirdHighestPeakKw));

        return new Schedule(
            allocations, realizedPeakKw, energyCostExVatOre + capacityDeltaExVatNok * 100m,
            remainingKwh <= 0m, Math.Max(0m, remainingKwh), "naive");
    }

    /// <summary>Best-effort classification of what shaped this schedule, for
    /// docs/DESIGN.md §4's "begrenset av ..." sentence -- not an exhaustive proof.</summary>
    private static string DetermineBindingConstraint(
        FlexibleLoad load,
        IReadOnlyDictionary<DateTimeOffset, decimal> marginalCost,
        decimal remainingKwh,
        decimal chosenCeilingKw,
        decimal currentThirdHighestPeakKw)
    {
        if (remainingKwh > 0m)
        {
            var candidateHourCount = marginalCost.Keys.Count(h => h >= load.NotBefore && h < load.Deadline);
            var maxPossibleAtFullPowerKwh = candidateHourCount * load.MaxPowerKw;
            return maxPossibleAtFullPowerKwh < load.EnergyKwh ? "deadline" : "capacity_step";
        }

        // Fully scheduled: the step only "bound" the answer if reaching this ceiling meant
        // exceeding the peak already locked in this month -- otherwise the schedule's shape
        // came purely from chasing cheap hours within existing headroom.
        return chosenCeilingKw > currentThirdHighestPeakKw ? "capacity_step" : "max_power";
    }

    private static Schedule Choose(Schedule? current, Schedule candidate)
    {
        if (current is null)
        {
            return candidate;
        }

        // Prefer fewer unscheduled kWh, then lower cost, then the lower ceiling (don't take
        // a step you don't need) as the final deterministic tie-break.
        if (candidate.UnscheduledKwh != current.UnscheduledKwh)
        {
            return candidate.UnscheduledKwh < current.UnscheduledKwh ? candidate : current;
        }

        if (candidate.TotalCostExVatOre != current.TotalCostExVatOre)
        {
            return candidate.TotalCostExVatOre < current.TotalCostExVatOre ? candidate : current;
        }

        return candidate.ChosenPeakCeilingKw < current.ChosenPeakCeilingKw ? candidate : current;
    }
}

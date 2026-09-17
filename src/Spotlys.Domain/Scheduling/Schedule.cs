namespace Spotlys.Domain.Scheduling;

/// <summary>One hour of a <see cref="Schedule"/>'s chosen allocation.</summary>
/// <param name="HourStartUtc">Start of the hour, UTC.</param>
/// <param name="AllocatedKwh">Energy allocated to this hour.</param>
/// <param name="MarginalCostExVatOrePerKwh">The per-kWh cost this hour was sorted by --
/// carried through so the caller can show the bill, not just the schedule.</param>
public sealed record HourAllocation(
    DateTimeOffset HourStartUtc,
    decimal AllocatedKwh,
    decimal MarginalCostExVatOrePerKwh);

/// <summary>
/// The result of <see cref="LoadOptimizer.Optimize"/>: which hours to charge in, at which
/// peak ceiling, and why (docs/DESIGN.md §4's "begrenset av ..." sentence).
/// </summary>
/// <param name="Allocations">Hours actually used, in no particular order.</param>
/// <param name="ChosenPeakCeilingKw">The peak kW this schedule actually realizes (baseline
/// plus allocated load, or the month's already-locked-in peak if higher) -- what the
/// capacity-step cost is priced against, not the self-imposed bound the outer loop explored
/// to get here (docs/FORECASTING.md §8's outer loop).</param>
/// <param name="TotalCostExVatOre">Energy cost plus the capacity-step delta vs. the peak
/// already locked in this month, in øre.</param>
/// <param name="IsFullyScheduled">False only when <see cref="FlexibleLoad.Deadline"/> or
/// <see cref="FlexibleLoad.MaxPowerKw"/> makes the full <see cref="FlexibleLoad.EnergyKwh"/>
/// unreachable within the window.</param>
/// <param name="UnscheduledKwh">0 when <see cref="IsFullyScheduled"/> is true.</param>
/// <param name="BindingConstraint">"deadline" | "capacity_step" | "max_power" -- a
/// best-effort label for what shaped this particular schedule, not an exhaustively-proven
/// classification. See <see cref="LoadOptimizer"/>'s own remarks for exactly how it's
/// derived.</param>
public sealed record Schedule(
    IReadOnlyList<HourAllocation> Allocations,
    decimal ChosenPeakCeilingKw,
    decimal TotalCostExVatOre,
    bool IsFullyScheduled,
    decimal UnscheduledKwh,
    string BindingConstraint);

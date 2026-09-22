using Spotlys.Application.Accounts;
using Spotlys.Application.Forecasting;
using Spotlys.Application.Metering;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Forecasting;
using Spotlys.Domain.Metering;
using Spotlys.Domain.Pricing;
using Spotlys.Domain.Scheduling;

namespace Spotlys.Application.Scheduling;

/// <summary>docs/FORECASTING.md §8: a range plus a counterfactual, structurally -- never a
/// bare number (CLAUDE.md rule 8).</summary>
public sealed record CostRange(decimal LowExVatNok, decimal HighExVatNok, decimal ExpectedExVatNok);

/// <summary>The comparison a plan's "instead" line names -- always present, never optional
/// (docs/FORECASTING.md §8: "never a single fake-precise number").</summary>
/// <param name="Label">Norwegian bokmål, matching docs/DESIGN.md §7's product language --
/// present-tense ("plugger inn nå i stedet"), not an infinitive, since the frontend embeds
/// it directly after "hvis du ...". Not yet a resource-file lookup (the whole app hardcodes
/// Norwegian strings today, a pre-existing gap docs/DESIGN.md §7 names and this phase
/// doesn't retrofit); stated so this label doesn't quietly become the one English string in
/// an otherwise-Norwegian UI.</param>
/// <param name="Cost">The cost range of the compared strategy.</param>
public sealed record Counterfactual(string Label, CostRange Cost);

/// <summary>The optimizer's answer, in the shape the API returns it.</summary>
/// <param name="Allocations">The chosen hours and how much to draw in each.</param>
/// <param name="ChosenCost">The chosen schedule's own cost range.</param>
/// <param name="Counterfactual">What the naive "plug in now" strategy would have cost.</param>
/// <param name="BindingConstraint">"deadline" | "capacity_step" | "max_power" --
/// <see cref="Domain.Scheduling.Schedule.BindingConstraint"/>'s own value, passed through.</param>
/// <param name="IsFullyScheduled">False only if the load's window or power ceiling made the
/// full requested energy unreachable.</param>
public sealed record PlanResult(
    IReadOnlyList<HourAllocation> Allocations,
    CostRange ChosenCost,
    Counterfactual Counterfactual,
    string BindingConstraint,
    bool IsFullyScheduled);

/// <summary>
/// Orchestrates docs/FORECASTING.md §8's optimizer: resolves the meter's own tariff and
/// support scheme, turns the live quantile forecast into the expected-cost array the pure
/// <see cref="LoadOptimizer"/> needs ("don't optimise the median"), then re-evaluates the
/// chosen hours at the forecast's q05/q95 tails for the range shown to the user, and against
/// <see cref="LoadOptimizer.NaiveImmediateSchedule"/> for the "plug in now instead"
/// counterfactual.
/// </summary>
public sealed class PlanChargeUseCase(
    IMeterProfileRepository meters,
    IGridTariffRepository gridTariffs,
    ISchemeParameterRepository schemeParameters,
    IForecastService forecastService,
    IConsumptionReadingRepository consumption,
    TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    // Trailing window for the "typical household draw" baseline -- see the module remarks
    // on BaselineLoadKw below for why this is a deliberate simplification, not a full
    // per-hour consumption forecast (out of scope this phase).
    private const int BaselineTrailingDays = 30;

    /// <summary>Null if the meter doesn't exist, has no tariff on file, or no active
    /// forecast model exists for its zone/regime -- the caller (the endpoint) turns each of
    /// those into the right Problem Details status, never a 500. Ownership is checked by the
    /// caller before this runs; this method trusts <paramref name="meterProfileId"/>.</summary>
    public async Task<PlanResult?> RunAsync(
        Guid meterProfileId, FlexibleLoad load, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(load);

        var meter = await meters.GetAsync(meterProfileId, ct).ConfigureAwait(false);
        if (meter is null)
        {
            return null;
        }

        var nowUtc = timeProvider.GetUtcNow();
        var forDateOslo = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, Oslo).Date);

        if (!Enum.TryParse<PriceArea>(meter.Zone, ignoreCase: true, out var zone))
        {
            return null;
        }

        var gridTariff = await gridTariffs.GetAsync(meter.GridCompanyId, forDateOslo, ct).ConfigureAwait(false);
        if (gridTariff is null)
        {
            return null;
        }

        SupportScheme scheme = meter.SupportScheme == "norgespris"
            ? new NorgesprisScheme(await schemeParameters.GetNorgesprisParametersAsync(forDateOslo, meter.IsCabin, ct).ConfigureAwait(false))
            : new StromstotteScheme(await schemeParameters.GetStromstotteParametersAsync(forDateOslo, ct).ConfigureAwait(false));

        var horizonHours = (int)Math.Clamp(Math.Ceiling((load.Deadline - nowUtc).TotalHours), 1, 168);
        var fan = await forecastService.GetForecastAsync(zone, nowUtc, horizonHours, ct).ConfigureAwait(false);
        if (fan is null)
        {
            return null;
        }

        var monthStartOslo = new DateOnly(forDateOslo.Year, forDateOslo.Month, 1);
        var monthStartLocal = DateTime.SpecifyKind(monthStartOslo.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var monthStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, Oslo), TimeSpan.Zero);
        var monthToDateReadings = await consumption.GetRangeAsync(meterProfileId, monthStartUtc, nowUtc, ct).ConfigureAwait(false);
        var monthToDateKwh = monthToDateReadings.Sum(r => r.Kwh);
        var peakSummary = PeakTracker.Summarize(monthToDateReadings, gridTariff.CapacitySteps);

        // BaselineLoadKw: the household's OTHER, non-flexible draw per hour -- needed so the
        // optimizer knows how much headroom is left under a candidate ceiling. There's no
        // per-hour consumption forecast in this system (a real one is out of scope this
        // phase), so this uses a single flat figure -- the trailing 30-day average hourly
        // draw -- for every target hour, rather than an hour-of-day-specific curve. Stated
        // simplification, not an oversight: the optimizer is already a greedy heuristic, not
        // an exact joint optimization, so a coarse baseline is consistent with its own
        // precision, not a new weak link.
        var trailingReadings = await consumption.GetRangeAsync(meterProfileId, nowUtc.AddDays(-BaselineTrailingDays), nowUtc, ct).ConfigureAwait(false);
        var baselineKwPerHour = trailingReadings.Count > 0 ? trailingReadings.Average(r => r.Kwh) : 0m;
        var baselineLoadKw = fan.Hours.ToDictionary(h => h.TargetHourUtc, _ => baselineKwPerHour);

        // The marginal cost per kWh, at each quantile of the forecast fan -- kwh=1m because
        // this is "the rate for the next kWh", not the load's own total allocation (which
        // the optimizer itself decides); monthToDateKwh is held fixed across every target
        // hour rather than projected forward hour by hour, a deliberate approximation for
        // the same reason as BaselineLoadKw above.
        decimal MarginalCost(decimal spotExVatOrePerKwh) =>
            scheme.EnergyCostExVatOrePerKwh(spotExVatOrePerKwh, meter.SupplierMarkupExVatOrePerKwh, 1m, monthToDateKwh);

        var expectedSpot = fan.ExpectedSpotExVatOrePerKwh();
        var expectedMarginalCost = expectedSpot.ToDictionary(kv => kv.Key, kv => MarginalCost(kv.Value));

        var schedule = LoadOptimizer.Optimize(load, expectedMarginalCost, baselineLoadKw, gridTariff.CapacitySteps, peakSummary.AverageKw);
        var naiveSchedule = LoadOptimizer.NaiveImmediateSchedule(load, expectedMarginalCost, baselineLoadKw, gridTariff.CapacitySteps, peakSummary.AverageKw);

        var byHour = fan.Hours.ToDictionary(h => h.TargetHourUtc);
        var chosenCost = RangeCost(schedule, byHour, MarginalCost);
        var naiveCost = RangeCost(naiveSchedule, byHour, MarginalCost);

        return new PlanResult(
            schedule.Allocations,
            chosenCost,
            new Counterfactual("plugger inn nå i stedet", naiveCost),
            schedule.BindingConstraint,
            schedule.IsFullyScheduled);
    }

    /// <summary>Re-evaluates a chosen schedule's cost at the forecast's q05 and q95 tails,
    /// without re-running the optimizer -- the allocation itself was chosen once, on the
    /// expected-cost array; only its cost is re-evaluated at the tails. The capacity term
    /// doesn't vary with price quantile, so it's backed out of the schedule's own
    /// TotalCostExVatOre once (via the energy cost the schedule already recorded per hour)
    /// and added back unchanged to both tails.</summary>
    private static CostRange RangeCost(
        Schedule schedule, Dictionary<DateTimeOffset, QuantileForecast> byHour, Func<decimal, decimal> marginalCost)
    {
        var recordedEnergyCostExVatOre = schedule.Allocations.Sum(a => a.AllocatedKwh * a.MarginalCostExVatOrePerKwh);
        var capacityDeltaExVatOre = schedule.TotalCostExVatOre - recordedEnergyCostExVatOre;

        decimal EnergyCostAt(Func<QuantileForecast, decimal> pickSpot)
        {
            var total = 0m;
            foreach (var allocation in schedule.Allocations)
            {
                if (byHour.TryGetValue(allocation.HourStartUtc, out var quantiles))
                {
                    total += allocation.AllocatedKwh * marginalCost(pickSpot(quantiles));
                }
            }

            return total;
        }

        var lowExVatOre = EnergyCostAt(q => q.Q05) + capacityDeltaExVatOre;
        var highExVatOre = EnergyCostAt(q => q.Q95) + capacityDeltaExVatOre;

        return new CostRange(
            Math.Min(lowExVatOre, highExVatOre) / 100m,
            Math.Max(lowExVatOre, highExVatOre) / 100m,
            schedule.TotalCostExVatOre / 100m);
    }
}

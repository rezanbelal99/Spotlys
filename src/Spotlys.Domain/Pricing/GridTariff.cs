namespace Spotlys.Domain.Pricing;

/// <summary>One capacity (kapasitetsledd) step -- a half-open interval <c>[FromKw, ToKw)</c>,
/// so a peak of exactly <see cref="ToKw"/> falls in the <em>next</em> step up.</summary>
public sealed record CapacityStep(decimal FromKw, decimal ToKw, decimal MonthlyExVatNok);

/// <summary>
/// One grid company's published tariff (docs/DOMAIN.md §4): day/night energiledd and the
/// capacity step table. Resolved by the caller from the versioned grid_tariff table for the
/// relevant date -- no bare rates in this file (CLAUDE.md rule 3).
/// </summary>
public sealed record GridTariff(
    string GridCompanyId,
    decimal EnergyDayExVatOrePerKwh,
    decimal EnergyNightExVatOrePerKwh,
    IReadOnlyList<CapacityStep> CapacitySteps
)
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    /// <summary>docs/DOMAIN.md §4b: weekday nights 22:00-06:00, all Saturdays/Sundays, and
    /// public holidays get the cheaper energiledd rate. <paramref name="publicHolidaysOslo"/>
    /// is supplied by the caller (Nager.Date-backed in Infrastructure) rather than looked
    /// up here, so this stays a pure function.</summary>
    public static bool IsNight(DateTimeOffset hourStartUtc, IReadOnlySet<DateOnly> publicHolidaysOslo)
    {
        ArgumentNullException.ThrowIfNull(publicHolidaysOslo);

        var local = TimeZoneInfo.ConvertTime(hourStartUtc, Oslo);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return true;
        }

        if (publicHolidaysOslo.Contains(DateOnly.FromDateTime(local.Date)))
        {
            return true;
        }

        return local.Hour >= 22 || local.Hour < 6;
    }

    /// <summary>The energiledd rate that applies to one hour.</summary>
    public decimal EnergileddRateExVatOrePerKwh(DateTimeOffset hourStartUtc, IReadOnlySet<DateOnly> publicHolidaysOslo) =>
        IsNight(hourStartUtc, publicHolidaysOslo) ? EnergyNightExVatOrePerKwh : EnergyDayExVatOrePerKwh;

    /// <summary>
    /// docs/DOMAIN.md §4a: the three highest daily peak hourly kWh values in the month,
    /// taken from three different days, ranked highest first. Grouping by Oslo-local
    /// calendar day and keeping only each day's single highest hour is what makes "three
    /// different days" automatic. Returns fewer than <paramref name="take"/> pairs if fewer
    /// days of consumption are supplied -- docs/DOMAIN.md doesn't specify a partial-month
    /// rule, and this is the natural generalisation. Used both by
    /// <see cref="CalculateTopThreeAverageKw"/> below and by
    /// <c>Spotlys.Domain.Metering.PeakTracker</c>, which needs the individual ranked days
    /// (not just their average) for the "Din topp: 4,2 kW" readout.
    /// </summary>
    public static IReadOnlyList<(DateOnly Day, decimal PeakKw)> RankedDailyPeaksKw(
        IReadOnlyList<HourlyConsumption> consumption, int take = 3)
    {
        ArgumentNullException.ThrowIfNull(consumption);

        return consumption
            .GroupBy(c => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.HourStartUtc, Oslo).Date))
            .Select(g => (Day: g.Key, PeakKw: g.Max(c => c.Kwh)))
            .OrderByDescending(p => p.PeakKw)
            .Take(take)
            .ToList();
    }

    /// <summary>The average of <see cref="RankedDailyPeaksKw"/>'s top 3 -- the figure the
    /// capacity-step lookup actually uses (docs/DOMAIN.md §4a).</summary>
    public static decimal CalculateTopThreeAverageKw(IReadOnlyList<HourlyConsumption> monthConsumption)
    {
        var dailyPeaks = RankedDailyPeaksKw(monthConsumption).Select(p => p.PeakKw).ToList();
        return dailyPeaks.Count == 0 ? 0m : dailyPeaks.Average();
    }

    /// <summary>Looks up the monthly kapasitetsledd for a given top-3 average kW against an
    /// arbitrary step table. A value at or above the highest defined step's
    /// <see cref="CapacityStep.ToKw"/> uses that step's rate rather than silently returning
    /// zero. Static so <c>Spotlys.Domain.Scheduling.LoadOptimizer</c> (no I/O, no
    /// <see cref="GridTariff"/> instance of its own -- just the step table) can reuse the
    /// exact same lookup instead of duplicating it.</summary>
    public static decimal CapacityStepMonthlyExVatNok(IReadOnlyList<CapacityStep> steps, decimal averageTopKw)
    {
        ArgumentNullException.ThrowIfNull(steps);

        foreach (var step in steps)
        {
            if (averageTopKw >= step.FromKw && averageTopKw < step.ToKw)
            {
                return step.MonthlyExVatNok;
            }
        }

        return steps.Count > 0 ? steps[^1].MonthlyExVatNok : 0m;
    }

    /// <inheritdoc cref="CapacityStepMonthlyExVatNok(IReadOnlyList{CapacityStep}, decimal)"/>
    public decimal CapacityStepMonthlyExVatNok(decimal averageTopKw) =>
        CapacityStepMonthlyExVatNok(CapacitySteps, averageTopKw);
}

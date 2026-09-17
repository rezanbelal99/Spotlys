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
    /// docs/DOMAIN.md §4a: the average of the three highest daily peak hourly kWh values in
    /// the month, taken from three different days. Grouping by Oslo-local calendar day and
    /// keeping only each day's single highest hour is what makes "three different days"
    /// automatic. If fewer than three days of consumption are supplied, averages over
    /// however many daily peaks exist -- docs/DOMAIN.md doesn't specify a partial-month
    /// rule, and this is the natural generalisation.
    /// </summary>
    public static decimal CalculateTopThreeAverageKw(IReadOnlyList<HourlyConsumption> monthConsumption)
    {
        ArgumentNullException.ThrowIfNull(monthConsumption);

        var dailyPeaks = monthConsumption
            .GroupBy(c => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.HourStartUtc, Oslo).Date))
            .Select(g => g.Max(c => c.Kwh))
            .OrderByDescending(peak => peak)
            .Take(3)
            .ToList();

        return dailyPeaks.Count == 0 ? 0m : dailyPeaks.Average();
    }

    /// <summary>Looks up the monthly kapasitetsledd for a given top-3 average kW. A value
    /// at or above the highest defined step's <see cref="CapacityStep.ToKw"/> uses that
    /// step's rate rather than silently returning zero.</summary>
    public decimal CapacityStepMonthlyExVatNok(decimal averageTopKw)
    {
        foreach (var step in CapacitySteps)
        {
            if (averageTopKw >= step.FromKw && averageTopKw < step.ToKw)
            {
                return step.MonthlyExVatNok;
            }
        }

        return CapacitySteps.Count > 0 ? CapacitySteps[^1].MonthlyExVatNok : 0m;
    }
}

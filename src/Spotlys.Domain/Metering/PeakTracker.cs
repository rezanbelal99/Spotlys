using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Metering;

/// <summary>docs/DOMAIN.md §4a's month-to-date peak picture: the ranked top-3 daily peaks,
/// their average (what the capacity step is actually priced against), and how much
/// headroom is left before the next, more expensive step.</summary>
/// <param name="TopThree">Ranked highest first -- <see cref="GridTariff.RankedDailyPeaksKw"/>'s
/// own output, one entry per of the (up to) three highest days.</param>
/// <param name="AverageKw">The figure <see cref="GridTariff.CapacityStepMonthlyExVatNok(System.Collections.Generic.IReadOnlyList{CapacityStep},decimal)"/>
/// is actually priced against.</param>
/// <param name="HeadroomToNextStepKw">How many more kW the month-to-date average could
/// absorb before crossing into the next, pricier step. 0 once already in the top defined
/// step -- there's no further boundary to report against.</param>
public sealed record MonthlyPeakSummary(
    IReadOnlyList<(DateOnly Day, decimal PeakKw)> TopThree,
    decimal AverageKw,
    decimal HeadroomToNextStepKw);

/// <summary>Turns raw month-to-date hourly consumption into the picture the UI's "Din topp"
/// readout and the optimizer's ceiling search both need.</summary>
public static class PeakTracker
{
    /// <summary>Builds the summary for one meter's month-to-date consumption.</summary>
    public static MonthlyPeakSummary Summarize(
        IReadOnlyList<HourlyConsumption> monthToDateConsumption, IReadOnlyList<CapacityStep> capacitySteps)
    {
        ArgumentNullException.ThrowIfNull(monthToDateConsumption);
        ArgumentNullException.ThrowIfNull(capacitySteps);

        var topThree = GridTariff.RankedDailyPeaksKw(monthToDateConsumption);
        var averageKw = topThree.Count == 0 ? 0m : topThree.Average(p => p.PeakKw);

        var headroom = 0m;
        for (var i = 0; i < capacitySteps.Count; i++)
        {
            var step = capacitySteps[i];
            if (averageKw >= step.FromKw && averageKw < step.ToKw)
            {
                // The top defined step reports zero headroom -- its own ToKw is typically a
                // large placeholder (e.g. this project's seeded tables use 999999 for "and
                // above"), not a real next boundary worth surfacing to a user.
                headroom = i == capacitySteps.Count - 1 ? 0m : step.ToKw - averageKw;
                break;
            }
        }

        return new MonthlyPeakSummary(topThree, averageKw, headroom);
    }
}

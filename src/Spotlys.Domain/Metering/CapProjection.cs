namespace Spotlys.Domain.Metering;

/// <summary>docs/DOMAIN.md §3b: a Norgespris household can cross the monthly cap mid-month
/// and become fully spot-exposed with zero support. This is the "you'll cross 5000 kWh
/// around the 24th" projection.</summary>
public static class CapProjection
{
    /// <summary>Linear extrapolation from a trailing daily average -- deliberately not a
    /// model: a transparent, explainable "if this week continues" statement (docs/DOMAIN.md
    /// §7's honesty constraint), not something the model page's skill claims apply to.
    /// Returns null if the current trailing rate won't cross the cap before month end;
    /// returns <paramref name="asOfDateOslo"/> itself if the cap is already crossed.</summary>
    /// <param name="monthToDateKwh">Cumulative consumption for the calendar month so far.</param>
    /// <param name="asOfDateOslo">Today's Oslo calendar date.</param>
    /// <param name="monthEndDateOslo">The last day of the current Oslo calendar month.</param>
    /// <param name="trailingDailyAverageKwh">The household's recent average daily
    /// consumption -- caller-computed (e.g. over the last 14 days).</param>
    /// <param name="capKwhPerMonth">docs/DOMAIN.md §3b: 5000 for homes, 1000 for cabins.</param>
    public static DateOnly? ProjectedCrossingDate(
        decimal monthToDateKwh,
        DateOnly asOfDateOslo,
        DateOnly monthEndDateOslo,
        decimal trailingDailyAverageKwh,
        decimal capKwhPerMonth)
    {
        if (monthToDateKwh >= capKwhPerMonth)
        {
            return asOfDateOslo;
        }

        if (trailingDailyAverageKwh <= 0m)
        {
            return null; // flat or falling consumption never projects a crossing
        }

        var remainingKwh = capKwhPerMonth - monthToDateKwh;
        var daysUntilCrossing = (double)(remainingKwh / trailingDailyAverageKwh);
        // Whole days only -- "around the 24th", not a fractional-day precision the
        // trailing-average method doesn't actually support.
        var wholeDaysUntilCrossing = (int)Math.Ceiling(daysUntilCrossing);

        var projectedDate = asOfDateOslo.AddDays(wholeDaysUntilCrossing);
        return projectedDate > monthEndDateOslo ? null : projectedDate;
    }
}

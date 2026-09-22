using Spotlys.Domain.Metering;

namespace Spotlys.Domain.Tests.Metering;

public class CapProjectionTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 15);
    private static readonly DateOnly MonthEnd = new(2026, 1, 31);

    [Fact]
    public void Already_crossed_the_cap_returns_todays_date()
    {
        var result = CapProjection.ProjectedCrossingDate(
            monthToDateKwh: 5100m, AsOf, MonthEnd, trailingDailyAverageKwh: 100m, capKwhPerMonth: 5000m);

        Assert.Equal(AsOf, result);
    }

    [Fact]
    public void Flat_consumption_never_projects_a_crossing()
    {
        var result = CapProjection.ProjectedCrossingDate(
            monthToDateKwh: 1000m, AsOf, MonthEnd, trailingDailyAverageKwh: 0m, capKwhPerMonth: 5000m);

        Assert.Null(result);
    }

    [Fact]
    public void Projects_a_real_crossing_date_within_the_month()
    {
        // 1000 kWh remaining to the cap, 100 kWh/day trailing -- crosses in 10 days.
        var result = CapProjection.ProjectedCrossingDate(
            monthToDateKwh: 4000m, AsOf, MonthEnd, trailingDailyAverageKwh: 100m, capKwhPerMonth: 5000m);

        Assert.Equal(AsOf.AddDays(10), result);
    }

    [Fact]
    public void A_crossing_projected_beyond_month_end_returns_null()
    {
        // Only 100 kWh/day remaining budget over 16 days left (Jan 15 -> Jan 31) at a rate
        // that would take far longer than the month has left.
        var result = CapProjection.ProjectedCrossingDate(
            monthToDateKwh: 1000m, AsOf, MonthEnd, trailingDailyAverageKwh: 10m, capKwhPerMonth: 5000m);

        Assert.Null(result);
    }

    [Fact]
    public void A_crossing_projected_exactly_on_month_end_is_returned()
    {
        var daysLeft = MonthEnd.DayNumber - AsOf.DayNumber;
        var remainingKwh = 500m;
        var dailyAverage = remainingKwh / daysLeft;

        var result = CapProjection.ProjectedCrossingDate(
            monthToDateKwh: 5000m - remainingKwh, AsOf, MonthEnd, dailyAverage, capKwhPerMonth: 5000m);

        Assert.Equal(MonthEnd, result);
    }
}

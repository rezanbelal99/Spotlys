using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Tests.Pricing;

public class PriceObservationTests
{
    [Fact]
    public void Two_observations_with_the_same_natural_key_are_equal()
    {
        // PriceObservation is a record specifically so value equality on the natural key
        // (zone, hour_start_utc, source) is free -- the repository's upsert logic
        // (docs/DATA.md §6) depends on this holding.
        var hourStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = new PriceObservation(PriceArea.NO2, hourStart, 100.0m, 11.5m, "hkso", hourStart);
        var b = new PriceObservation(PriceArea.NO2, hourStart, 100.0m, 11.5m, "hkso", hourStart);

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(PriceArea.NO1)]
    [InlineData(PriceArea.NO2)]
    [InlineData(PriceArea.NO3)]
    [InlineData(PriceArea.NO4)]
    [InlineData(PriceArea.NO5)]
    public void All_five_bidding_zones_are_defined(PriceArea zone)
    {
        // docs/DOMAIN.md §1: Norway has exactly five day-ahead bidding zones.
        Assert.True(Enum.IsDefined(zone));
    }
}

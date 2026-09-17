namespace Spotlys.Infrastructure.Hydrology;

internal sealed class HydrologyWeekNormRow
{
    public required string Zone { get; set; }
    public int IsoWeek { get; set; }
    public float MinFillFraction { get; set; }
    public float MedianFillFraction { get; set; }
    public float MaxFillFraction { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
}

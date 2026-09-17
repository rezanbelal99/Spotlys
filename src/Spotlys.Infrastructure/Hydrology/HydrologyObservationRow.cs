namespace Spotlys.Infrastructure.Hydrology;

internal sealed class HydrologyObservationRow
{
    public required string Zone { get; set; }
    public DateOnly WeekStartDate { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
    public float FillFraction { get; set; }
    public float CapacityTwh { get; set; }
    public float FillTwh { get; set; }
    public DateTimeOffset? NextPublicationUtc { get; set; }
    public required string Source { get; set; }
}

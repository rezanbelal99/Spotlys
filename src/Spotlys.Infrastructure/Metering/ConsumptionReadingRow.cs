namespace Spotlys.Infrastructure.Metering;

/// <summary>docs/DATA.md §4: personal consumption data, stored in its own schema (see
/// <see cref="ConsumptionReadingEntityConfiguration"/>) with its own retention policy.</summary>
internal sealed class ConsumptionReadingRow
{
    public required Guid MeterProfileId { get; set; }
    public required DateTimeOffset HourStartUtc { get; set; }
    public required decimal Kwh { get; set; }
    public required string Source { get; set; } // 'csv_import' this phase (DATA.md §4's ladder)
    public DateTimeOffset ImportedAtUtc { get; set; }
}

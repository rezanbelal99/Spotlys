namespace Spotlys.Infrastructure.Pricing;

/// <summary>One capacity step as stored in the grid_tariff.capacity_steps jsonb column.
/// Public (not internal) only because System.Text.Json needs to see it to
/// (de)serialize -- never referenced outside this assembly.</summary>
public sealed record CapacityStepRow(decimal FromKw, decimal ToKw, decimal MonthlyExVatNok);

// EF entity for grid_tariff. ARCHITECTURE.md §4's DDL plus source_url, which that DDL
// doesn't have but this phase's own requirement ("seeded for three grid companies with
// source URLs") does -- a deliberate, documented extension of the schema as given.
internal sealed class GridTariffRow
{
    public required string GridCompanyId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public decimal EnergyDayOre { get; set; }
    public decimal EnergyNightOre { get; set; }
    public required List<CapacityStepRow> CapacitySteps { get; set; }
    public required string SourceUrl { get; set; }
}

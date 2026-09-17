namespace Spotlys.Infrastructure.Pricing;

// EF entity for grid_company. Referenced by grid_tariff.grid_company_id -- implied but not
// spelled out by ARCHITECTURE.md §4's DDL (meter_profile.grid_company_id REFERENCES it).
internal sealed class GridCompanyRow
{
    public required string Id { get; set; }
    public required string Name { get; set; }
}

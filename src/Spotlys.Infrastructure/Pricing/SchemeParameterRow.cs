namespace Spotlys.Infrastructure.Pricing;

// EF entity for scheme_parameter, exactly ARCHITECTURE.md §4's DDL. "Scheme parameters are
// data, not code" (docs/DOMAIN.md §7) -- this table is the only place a threshold, rate or
// cap is allowed to live.
internal sealed class SchemeParameterRow
{
    public required string Scheme { get; set; }
    public required string Parameter { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal Value { get; set; }
    public required string SourceUrl { get; set; }
}

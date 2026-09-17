namespace Spotlys.Domain.Pricing;

/// <summary>
/// Strømstøtte parameters as of a given date (docs/DOMAIN.md §3a). Always resolved by the
/// caller from the versioned <c>scheme_parameter</c> table -- never hard-coded (CLAUDE.md
/// rule 3).
/// </summary>
/// <param name="ThresholdExVatOrePerKwh">The støtte threshold, øre/kWh excluding VAT.</param>
/// <param name="SupportRate">Fraction of the above-threshold spread the state covers, 0..1.</param>
public sealed record StromstotteParameters(
    decimal ThresholdExVatOrePerKwh,
    decimal SupportRate
);

/// <summary>Norgespris parameters as of a given date (docs/DOMAIN.md §3b).</summary>
/// <param name="RateExVatOrePerKwh">The flat rate, øre/kWh excluding VAT.</param>
/// <param name="CapKwhPerMonth">Monthly cap -- 5000 for homes, 1000 for cabins.</param>
public sealed record NorgesprisParameters(
    decimal RateExVatOrePerKwh,
    decimal CapKwhPerMonth
);

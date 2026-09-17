namespace Spotlys.Domain.Pricing;

/// <summary>
/// Per-kWh national levies plus VAT (docs/DOMAIN.md §4c), resolved by the caller from the
/// versioned scheme_parameter table for the relevant date.
///
/// Enova is modelled here as per-kWh rather than the fixed annual amount docs/DOMAIN.md
/// describes: three real grid companies' current published price pages (Glitre Nett,
/// Elvia, L-nett) all bill it as ~1.0 øre/kWh ex VAT alongside forbruksavgift, and no fixed
/// annual figure is findable anywhere -- confirmed with the user during Phase 2 planning.
/// </summary>
/// <param name="ForbruksavgiftExVatOrePerKwh">Elavgift, øre/kWh ex VAT. Time-versioned --
/// changes annually and sometimes mid-year.</param>
/// <param name="EnovaExVatOrePerKwh">Enova levy, øre/kWh ex VAT.</param>
/// <param name="VatRate">0.25 standard; 0 in NO4 (docs/DOMAIN.md §1).</param>
public sealed record LevyParameters(
    decimal ForbruksavgiftExVatOrePerKwh,
    decimal EnovaExVatOrePerKwh,
    decimal VatRate
);

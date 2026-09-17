namespace Spotlys.Domain.Pricing;

/// <summary>
/// The strømstøtte/Norgespris switch (docs/DOMAIN.md §3). Pure: no DB access, no ambient
/// time. Every rate comes from <see cref="StromstotteParameters"/> or
/// <see cref="NorgesprisParameters"/>, resolved by the caller from the versioned
/// scheme_parameter table -- never a bare literal in this file (CLAUDE.md rule 3).
/// </summary>
public abstract record SupportScheme
{
    /// <summary>
    /// Ex-VAT øre/kWh cost of energy for one hour -- before nettleie, before VAT. Callers
    /// multiply by the hour's kWh to get that hour's energy line.
    /// </summary>
    /// <param name="spotExVatOrePerKwh">The hour's day-ahead spot price, excluding VAT.</param>
    /// <param name="supplierMarkupExVatOrePerKwh">Supplier's påslag, excluding VAT.</param>
    /// <param name="kwh">This hour's consumption -- needed by Norgespris to test the
    /// monthly cap; unused by strømstøtte, whose per-kWh rate doesn't depend on quantity.</param>
    /// <param name="monthToDateKwhBeforeThisHour">Cumulative consumption for the calendar
    /// month strictly before this hour.</param>
    public abstract decimal EnergyCostExVatOrePerKwh(
        decimal spotExVatOrePerKwh,
        decimal supplierMarkupExVatOrePerKwh,
        decimal kwh,
        decimal monthToDateKwhBeforeThisHour);
}

/// <summary>
/// Ordinary strømstøtte (docs/DOMAIN.md §3a): hourly, the state covers
/// <see cref="StromstotteParameters.SupportRate"/> of the amount above
/// <see cref="StromstotteParameters.ThresholdExVatOrePerKwh"/>. The tariff engine computes
/// marginal cost after support, never raw spot spread -- this formula is the whole reason
/// that's true: above the threshold, only <c>1 - SupportRate</c> of any further increase
/// reaches the customer.
/// </summary>
public sealed record StromstotteScheme(StromstotteParameters Parameters) : SupportScheme
{
    /// <inheritdoc />
    public override decimal EnergyCostExVatOrePerKwh(
        decimal spotExVatOrePerKwh,
        decimal supplierMarkupExVatOrePerKwh,
        decimal kwh,
        decimal monthToDateKwhBeforeThisHour)
    {
        var stotteExVatOrePerKwh = Parameters.SupportRate
            * Math.Max(0m, spotExVatOrePerKwh - Parameters.ThresholdExVatOrePerKwh);

        return spotExVatOrePerKwh + supplierMarkupExVatOrePerKwh - stotteExVatOrePerKwh;
    }
}

/// <summary>
/// Norgespris (docs/DOMAIN.md §3b): a flat, spot-independent rate up to
/// <see cref="NorgesprisParameters.CapKwhPerMonth"/> kWh in the month; above the cap, no
/// support at all -- full spot plus supplier markup. The cap check is on the cumulative
/// total <em>including</em> this hour's kWh, so an hour that would cross the cap is billed
/// entirely at the no-support rate rather than pro-rated within the hour -- matching
/// docs/DOMAIN.md §3b's pseudocode exactly, not smoothed over.
/// </summary>
public sealed record NorgesprisScheme(NorgesprisParameters Parameters) : SupportScheme
{
    /// <inheritdoc />
    public override decimal EnergyCostExVatOrePerKwh(
        decimal spotExVatOrePerKwh,
        decimal supplierMarkupExVatOrePerKwh,
        decimal kwh,
        decimal monthToDateKwhBeforeThisHour)
    {
        var cumulativeKwh = monthToDateKwhBeforeThisHour + kwh;

        return cumulativeKwh <= Parameters.CapKwhPerMonth
            ? Parameters.RateExVatOrePerKwh
            : spotExVatOrePerKwh + supplierMarkupExVatOrePerKwh;
    }
}

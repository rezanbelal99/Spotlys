namespace Spotlys.Domain.Pricing;

/// <summary>One hour's contribution to the bill -- what the UI's hour readout shows
/// (CLAUDE.md rule 1: never the raw spot price).</summary>
/// <param name="HourStartUtc">Start of the hour, UTC.</param>
/// <param name="SpotExVatOrePerKwh">The raw spot price, kept for transparency/debugging --
/// never shown to a user as "their price" on its own.</param>
/// <param name="Kwh">Consumption for the hour.</param>
/// <param name="EnergyCostExVatOre">This hour's energy line (spot ± regime, incl. påslag),
/// total øre for the hour -- not a per-kWh rate.</param>
/// <param name="EnergileddExVatOre">This hour's day/night grid energy charge, total øre.</param>
/// <param name="IsNightRate">Whether the cheaper night/weekend/holiday energiledd rate applied.</param>
public sealed record HourlyBillLine(
    DateTimeOffset HourStartUtc,
    decimal SpotExVatOrePerKwh,
    decimal Kwh,
    decimal EnergyCostExVatOre,
    decimal EnergileddExVatOre,
    bool IsNightRate
)
{
    /// <summary>Total ex-VAT cost for the hour (energy + energiledd), in øre -- the figure
    /// the UI's hour readout shows. Levies and kapasitetsledd are month-level, not
    /// attributable to a single hour, so they're not included here.</summary>
    public decimal TotalExVatOre => EnergyCostExVatOre + EnergileddExVatOre;
}

/// <summary>
/// A full month's itemised bill, matching docs/DOMAIN.md §5's formula line for line. Every
/// number a user sees comes out of this type (CLAUDE.md rule 1) -- constructed only by
/// <see cref="TariffEngine"/>.
/// </summary>
public sealed record Bill(
    decimal EnergyExVatNok,
    decimal EnergileddExVatNok,
    decimal KapasitetsleddExVatNok,
    decimal ForbruksavgiftExVatNok,
    decimal EnovaExVatNok,
    decimal SupplierFeeExVatNok,
    decimal VatRate,
    decimal TotalExVatNok,
    decimal TotalIncVatNok,
    decimal TopThreeAverageKw,
    IReadOnlyList<HourlyBillLine> HourlyLines
);

namespace Spotlys.Domain.Pricing;

/// <summary>
/// One hour's day-ahead spot price, exactly as published (docs/DATA.md §1) --
/// excluding VAT, no support scheme or tariff applied. Never display this directly to a
/// user as "their price" (CLAUDE.md rule 1); it only exists to feed
/// <c>Spotlys.Domain.Pricing.TariffEngine</c> once that exists (Phase 2).
/// </summary>
/// <param name="Zone">The bidding zone this price applies to.</param>
/// <param name="HourStartUtc">Start of the hour this price covers, in UTC.</param>
/// <param name="PriceExVatOrePerKwh">øre/kWh, excluding VAT, as published by the source.</param>
/// <param name="CurrencyRate">The EUR-&gt;NOK rate the source used for this day, if known.</param>
/// <param name="Source">Which upstream this observation came from, e.g. "hkso".</param>
/// <param name="ObservedAtUtc">When Spotlys itself recorded this row -- not when the price
/// was published upstream. Distinct from <see cref="HourStartUtc"/> per docs/DATA.md's
/// point-in-time rule.</param>
public sealed record PriceObservation(
    PriceArea Zone,
    DateTimeOffset HourStartUtc,
    decimal PriceExVatOrePerKwh,
    decimal? CurrencyRate,
    string Source,
    DateTimeOffset ObservedAtUtc
);

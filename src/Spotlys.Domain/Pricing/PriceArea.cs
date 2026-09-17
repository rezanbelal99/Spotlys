namespace Spotlys.Domain.Pricing;

/// <summary>
/// One of Norway's five day-ahead bidding zones (docs/DOMAIN.md §1). NO4 is a distinct
/// VAT regime (no VAT on electricity) -- callers must not assume a single VAT rate applies
/// across all zones.
/// </summary>
public enum PriceArea
{
    /// <summary>Sørøst-Norge (Oslo).</summary>
    NO1,

    /// <summary>Sørvest-Norge (Kristiansand, Stavanger). Most continental coupling.</summary>
    NO2,

    /// <summary>Midt-Norge (Trondheim).</summary>
    NO3,

    /// <summary>Nord-Norge (Tromsø). No VAT on electricity -- a distinct VAT regime.</summary>
    NO4,

    /// <summary>Vestlandet (Bergen).</summary>
    NO5,
}

using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Api.Pricing;

/// <summary>
/// The one place that resolves versioned scheme/tariff/holiday data and hands it to the
/// pure <see cref="TariffEngine"/> -- shared by <c>POST /bill/simulate</c> and
/// <c>GET /demo/bill</c> so both go through the exact same orchestration.
/// </summary>
internal static class BillCalculator
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    /// <exception cref="InvalidOperationException">The grid company is unknown, has no
    /// tariff effective on the bill's date, or a consumption hour has no matching spot
    /// price -- all client-input problems, mapped to Problem Details by the caller.</exception>
    public static async Task<Bill> CalculateAsync(
        PriceArea zone,
        string gridCompanyId,
        bool isNorgespris,
        bool isCabin,
        decimal supplierMarkupExVatOrePerKwh,
        decimal supplierMonthlyFeeExVatNok,
        IReadOnlyList<HourlyConsumption> consumption,
        ISchemeParameterRepository schemeParameters,
        IGridTariffRepository gridTariffs,
        IPriceObservationRepository prices,
        IPublicHolidayProvider holidayProvider,
        CancellationToken ct)
    {
        var sorted = consumption.OrderBy(c => c.HourStartUtc).ToList();
        var forDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(sorted[0].HourStartUtc, Oslo).Date);

        SupportScheme scheme = isNorgespris
            ? new NorgesprisScheme(await schemeParameters.GetNorgesprisParametersAsync(forDate, isCabin, ct).ConfigureAwait(false))
            : new StromstotteScheme(await schemeParameters.GetStromstotteParametersAsync(forDate, ct).ConfigureAwait(false));

        var levies = await schemeParameters.GetLevyParametersAsync(forDate, zone, ct).ConfigureAwait(false);

        var gridTariff = await gridTariffs.GetAsync(gridCompanyId, forDate, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Unknown grid company '{gridCompanyId}', or it has no tariff effective {forDate:O}.");

        var holidays = sorted
            .Select(c => TimeZoneInfo.ConvertTime(c.HourStartUtc, Oslo).Year)
            .Distinct()
            .SelectMany(holidayProvider.GetHolidays)
            .ToHashSet();

        var fromUtc = sorted[0].HourStartUtc;
        var toUtc = sorted[^1].HourStartUtc.AddHours(1);
        var observations = await prices.GetRangeAsync(zone, fromUtc, toUtc, ct).ConfigureAwait(false);
        var spotPrices = observations.ToDictionary(o => o.HourStartUtc, o => o.PriceExVatOrePerKwh);

        return TariffEngine.CalculateBill(
            scheme,
            gridTariff,
            levies,
            supplierMarkupExVatOrePerKwh,
            supplierMonthlyFeeExVatNok,
            sorted,
            spotPrices,
            holidays);
    }
}

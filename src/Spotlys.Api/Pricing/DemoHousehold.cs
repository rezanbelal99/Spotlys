using Spotlys.Domain.Pricing;

namespace Spotlys.Api.Pricing;

/// <summary>
/// The seeded fictional household demo mode serves so a recruiter can explore the app
/// without signing up (docs/ARCHITECTURE.md §6: "Build this in Phase 2, not as an
/// afterthought"). NO2, Glitre Nett, ordinary strømstøtte -- the strongest segment for a
/// demo per docs/DOMAIN.md §6's own advice table.
///
/// The consumption shape is not invented: <c>Pricing/DemoData/no2-household-2026-08.csv</c>
/// was generated once from Elhub Open Data's real published NO2 household-average series
/// (<c>spike/data/elhub_open/consumptionPerGroupMbaHour</c>, PRICE_AREA=NO2,
/// CONSUMPTION_GROUP=household, August 2026 -- chosen because it's the most recent full
/// month on/after Glitre Nett's real seeded tariff's 2026-07-01 effective date), dividing
/// each hour's QUANTITY_KWH by its METERING_POINT_COUNT to get one average household's kWh
/// for that hour. That's a population average, not one real meter -- the same honesty
/// caveat as Phase 0's cold-start synthesis (docs/DATA.md §4) -- but it is genuinely
/// measured demand, not a hand-waved curve. The file is embedded in the assembly (not read
/// from spike/, which is dockerignored and absent from the deployed image) and matches the
/// public bill/simulate CSV contract exactly, so it's parsed by the same
/// <see cref="ConsumptionCsvParser"/>.
/// </summary>
internal static class DemoHousehold
{
    public const string GridCompanyId = "glitre-nett";
    public const PriceArea Zone = PriceArea.NO2;

    // A round, plausible supplier agreement -- not sourced, since no specific real supplier
    // backs this fictional household; the seeded scheme/grid rates above it are all real.
    public const decimal SupplierMarkupExVatOrePerKwh = 0m;
    public const decimal SupplierMonthlyFeeExVatNok = 39m;

    private const string ResourceName = "Spotlys.Api.Pricing.DemoData.no2-household-2026-08.csv";

    /// <summary>The only month currently seeded. Extending demo mode to other months means
    /// generating and embedding another CSV the same way, not writing new code.</summary>
    public static readonly (int Year, int Month) AvailableMonth = (2026, 8);

    public static async Task<IReadOnlyList<HourlyConsumption>> LoadMonthAsync(CancellationToken ct)
    {
        var stream = typeof(DemoHousehold).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        try
        {
            var result = await ConsumptionCsvParser.ParseAsync(stream, ct).ConfigureAwait(false);
            return result.Consumption
                ?? throw new InvalidOperationException("The embedded demo consumption file failed to parse.");
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}

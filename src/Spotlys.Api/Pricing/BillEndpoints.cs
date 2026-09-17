using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.OutputCaching;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Api.Pricing;

/// <summary>One hour's line on the bill -- what the UI's hour readout shows. Never the raw
/// spot price alone as "the price" (CLAUDE.md rule 1); <see cref="TotalExVatOre"/> is.</summary>
internal sealed record HourlyBillLineDto(
    DateTimeOffset HourStartUtc,
    decimal SpotExVatOrePerKwh,
    decimal Kwh,
    decimal EnergyCostExVatOre,
    decimal EnergileddExVatOre,
    decimal TotalExVatOre,
    bool IsNightRate)
{
    public static HourlyBillLineDto FromDomain(HourlyBillLine line) => new(
        line.HourStartUtc, line.SpotExVatOrePerKwh, line.Kwh, line.EnergyCostExVatOre,
        line.EnergileddExVatOre, line.TotalExVatOre, line.IsNightRate);
}

/// <summary>The itemised monthly bill (docs/DOMAIN.md §5), on the wire.</summary>
internal sealed record BillDto(
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
    IReadOnlyList<HourlyBillLineDto> HourlyLines)
{
    public static BillDto FromDomain(Bill bill) => new(
        bill.EnergyExVatNok, bill.EnergileddExVatNok, bill.KapasitetsleddExVatNok,
        bill.ForbruksavgiftExVatNok, bill.EnovaExVatNok, bill.SupplierFeeExVatNok,
        bill.VatRate, bill.TotalExVatNok, bill.TotalIncVatNok, bill.TopThreeAverageKw,
        bill.HourlyLines.Select(HourlyBillLineDto.FromDomain).ToList());
}

/// <summary>The demo bill plus the strømstøtte break-even, for the ribbon's colour-ramp
/// anchor (docs/DESIGN.md §1) -- not part of <see cref="Bill"/> itself, since it's a scheme
/// parameter, not a bill line.</summary>
internal sealed record DemoBillDto(BillDto Bill, decimal BreakEvenExVatOrePerKwh);

internal static class BillEndpoints
{
    // A month of hourly rows is a few hundred bytes short of ~30 KB; this is generous
    // headroom while still an explicit, hard bound (docs/ARCHITECTURE.md §9).
    private const long MaxConsumptionCsvBytes = 512 * 1024;

    private static readonly string[] ValidSchemes = ["stromstotte", "norgespris"];

    public static RouteGroupBuilder MapBillEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/bill/simulate", SimulateAsync)
            .WithName("SimulateBill")
            .RequireRateLimiting(RateLimiting.StrictPolicy)
            .Accepts<string>("text/csv");

        group.MapGet("/demo/bill", GetDemoBillAsync)
            .WithName("GetDemoBill")
            .CacheOutput(policy => policy
                .Expire(TimeSpan.FromMinutes(5))
                .SetVaryByQuery("year", "month")
                .Tag("demo-bill"));

        return group;
    }

    private static async Task<IResult> SimulateAsync(
        HttpRequest request,
        string zone,
        string gridCompanyId,
        string scheme,
        bool isCabin,
        decimal supplierMarkupExVatOrePerKwh,
        decimal supplierMonthlyFeeExVatNok,
        ISchemeParameterRepository schemeParameters,
        IGridTariffRepository gridTariffs,
        IPriceObservationRepository prices,
        IPublicHolidayProvider holidayProvider,
        CancellationToken ct)
    {
        if (!Enum.TryParse<PriceArea>(zone, ignoreCase: true, out var priceArea))
        {
            return Results.Problem(
                title: "Unknown price zone",
                detail: $"'{zone}' is not one of NO1..NO5.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ValidSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase))
        {
            return Results.Problem(
                title: "Unknown support scheme",
                detail: "'scheme' must be 'stromstotte' or 'norgespris'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = MaxConsumptionCsvBytes;
        }

        var parsed = await ConsumptionCsvParser.ParseAsync(request.Body, ct).ConfigureAwait(false);
        if (parsed.Error is not null)
        {
            return parsed.Error;
        }

        var isNorgespris = scheme.Equals("norgespris", StringComparison.OrdinalIgnoreCase);

        try
        {
            var bill = await BillCalculator.CalculateAsync(
                priceArea, gridCompanyId, isNorgespris, isCabin,
                supplierMarkupExVatOrePerKwh, supplierMonthlyFeeExVatNok,
                parsed.Consumption!, schemeParameters, gridTariffs, prices, holidayProvider, ct)
                .ConfigureAwait(false);

            return Results.Ok(BillDto.FromDomain(bill));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Could not compute bill",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<IResult> GetDemoBillAsync(
        int year,
        int month,
        ISchemeParameterRepository schemeParameters,
        IGridTariffRepository gridTariffs,
        IPriceObservationRepository prices,
        IPublicHolidayProvider holidayProvider,
        CancellationToken ct)
    {
        if ((year, month) != DemoHousehold.AvailableMonth)
        {
            return Results.Problem(
                title: "No demo data for that month",
                detail: $"Demo mode currently only has seeded consumption for " +
                    $"{DemoHousehold.AvailableMonth.Year:D4}-{DemoHousehold.AvailableMonth.Month:D2}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var consumption = await DemoHousehold.LoadMonthAsync(ct).ConfigureAwait(false);

        try
        {
            var bill = await BillCalculator.CalculateAsync(
                DemoHousehold.Zone, DemoHousehold.GridCompanyId, isNorgespris: false, isCabin: false,
                DemoHousehold.SupplierMarkupExVatOrePerKwh, DemoHousehold.SupplierMonthlyFeeExVatNok,
                consumption, schemeParameters, gridTariffs, prices, holidayProvider, ct)
                .ConfigureAwait(false);

            var firstOfMonth = DateOnly.FromDateTime(new DateTime(year, month, 1));
            var stromstotte = await schemeParameters.GetStromstotteParametersAsync(firstOfMonth, ct).ConfigureAwait(false);

            return Results.Ok(new DemoBillDto(BillDto.FromDomain(bill), stromstotte.ThresholdExVatOrePerKwh));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Could not compute demo bill",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }
}

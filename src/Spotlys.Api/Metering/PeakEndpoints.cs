using System.Security.Claims;
using Spotlys.Api.Accounts;
using Spotlys.Application.Accounts;
using Spotlys.Application.Metering;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Metering;

namespace Spotlys.Api.Metering;

internal sealed record DailyPeakDto(DateOnly Day, decimal PeakKw);

internal sealed record NorgesprisCapProjectionDto(decimal MonthToDateKwh, decimal CapKwhPerMonth, DateOnly? ProjectedCrossingDate);

internal sealed record MonthlyPeaksDto(
    IReadOnlyList<DailyPeakDto> TopThree,
    decimal AverageKw,
    decimal HeadroomToNextStepKw,
    decimal CurrentCapacityStepMonthlyExVatNok,
    NorgesprisCapProjectionDto? NorgesprisCapProjection);

internal static class PeakEndpoints
{
    // docs/DOMAIN.md §3b: CapProjection's own trailing-average input window.
    private const int TrailingAverageDays = 14;

    public static RouteGroupBuilder MapPeakEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/meters/{meterProfileId:guid}/peaks", GetPeaksAsync)
            .WithName("GetMeterPeaks")
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetPeaksAsync(
        Guid meterProfileId,
        ClaimsPrincipal user,
        IMeterProfileRepository meters,
        IConsumptionReadingRepository consumption,
        IGridTariffRepository gridTariffs,
        ISchemeParameterRepository schemeParameters,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var meter = await meters.GetAsync(meterProfileId, ct).ConfigureAwait(false);
        if (meter is null || meter.UserId != CurrentUser.Id(user))
        {
            // 404, not 403 -- see ConsumptionImportEndpoints' own remark on this pattern.
            return Results.Problem(title: "Meter not found", statusCode: StatusCodes.Status404NotFound);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
        var nowOslo = TimeZoneInfo.ConvertTime(nowUtc, oslo);
        var todayOslo = DateOnly.FromDateTime(nowOslo.DateTime);
        var monthStartOslo = new DateOnly(todayOslo.Year, todayOslo.Month, 1);
        var monthStartLocal = DateTime.SpecifyKind(monthStartOslo.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var monthStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, oslo), TimeSpan.Zero);

        var monthToDateConsumption = await consumption.GetRangeAsync(meterProfileId, monthStartUtc, nowUtc, ct).ConfigureAwait(false);

        var gridTariff = await gridTariffs.GetAsync(meter.GridCompanyId, todayOslo, ct).ConfigureAwait(false);
        if (gridTariff is null)
        {
            return Results.Problem(title: "No tariff on file for this meter's grid company", statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var summary = PeakTracker.Summarize(monthToDateConsumption, gridTariff.CapacitySteps);
        var currentCapacityCost = gridTariff.CapacityStepMonthlyExVatNok(summary.AverageKw);

        NorgesprisCapProjectionDto? capProjection = null;
        if (meter.SupportScheme == "norgespris")
        {
            var norgespris = await schemeParameters.GetNorgesprisParametersAsync(todayOslo, meter.IsCabin, ct).ConfigureAwait(false);
            var monthToDateKwh = monthToDateConsumption.Sum(c => c.Kwh);
            var trailingAverage = ConsumptionSeries.TrailingDailyAverageKwh(monthToDateConsumption, nowUtc, TrailingAverageDays);
            var monthEndOslo = monthStartOslo.AddMonths(1).AddDays(-1);
            var crossingDate = CapProjection.ProjectedCrossingDate(
                monthToDateKwh, todayOslo, monthEndOslo, trailingAverage, norgespris.CapKwhPerMonth);

            capProjection = new NorgesprisCapProjectionDto(monthToDateKwh, norgespris.CapKwhPerMonth, crossingDate);
        }

        var dto = new MonthlyPeaksDto(
            summary.TopThree.Select(p => new DailyPeakDto(p.Day, p.PeakKw)).ToList(),
            summary.AverageKw, summary.HeadroomToNextStepKw, currentCapacityCost, capProjection);

        return Results.Ok(dto);
    }
}

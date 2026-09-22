using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Spotlys.Api.Accounts;
using Spotlys.Api.Pricing;
using Spotlys.Application.Accounts;
using Spotlys.Application.Metering;

namespace Spotlys.Api.Metering;

internal sealed record ConsumptionImportResultDto(int RowsImported);

internal static class ConsumptionImportEndpoints
{
    // Same bound BillEndpoints.SimulateAsync uses for the anonymous, non-persisting
    // consumption upload -- this is the same shape of request (a year of hourly rows).
    private const long MaxConsumptionCsvBytes = 512 * 1024;

    public static RouteGroupBuilder MapConsumptionImportEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/meters/{meterProfileId:guid}/consumption:import", ImportAsync)
            .WithName("ImportConsumption")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiting.StrictPolicy)
            .Accepts<string>("text/csv");

        return group;
    }

    private static async Task<IResult> ImportAsync(
        Guid meterProfileId,
        HttpRequest request,
        ClaimsPrincipal user,
        IMeterProfileRepository meters,
        IConsumptionReadingRepository consumption,
        CancellationToken ct)
    {
        var meter = await meters.GetAsync(meterProfileId, ct).ConfigureAwait(false);
        if (meter is null)
        {
            return Results.Problem(title: "Meter not found", statusCode: StatusCodes.Status404NotFound);
        }

        if (meter.UserId != CurrentUser.Id(user))
        {
            // 404, not 403 -- doesn't confirm to a caller that a meter id they don't own
            // exists at all.
            return Results.Problem(title: "Meter not found", statusCode: StatusCodes.Status404NotFound);
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

        await consumption.UpsertRangeAsync(meterProfileId, parsed.Consumption!, "csv_import", ct).ConfigureAwait(false);

        return Results.Ok(new ConsumptionImportResultDto(parsed.Consumption!.Count));
    }
}

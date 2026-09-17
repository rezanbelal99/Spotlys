using Microsoft.AspNetCore.OutputCaching;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Api.Pricing;

/// <summary>ex-VAT, matching docs/DOMAIN.md's storage convention -- never the price a user
/// actually pays (CLAUDE.md rule 1). The name carries the unit and VAT status per CLAUDE.md
/// rule 2, since this is the one place in the system a raw spot price is allowed to exist
/// on the wire, for the tariff engine (or, until it ships in Phase 2, the placeholder
/// ribbon) to consume -- never rendered to a user as "your price" from here.</summary>
internal sealed record PriceObservationDto(
    string Zone,
    DateTimeOffset HourStartUtc,
    decimal PriceExVatOrePerKwh,
    string Source
);

internal static class PricingEndpoints
{
    private const string CacheTagPrefix = "prices-";

    public static RouteGroupBuilder MapPricingEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/prices/{zone}", GetPricesAsync)
            .WithName("GetPrices")
            .CacheOutput(policy => policy
                .Expire(TimeSpan.FromMinutes(5))
                .SetVaryByRouteValue("zone")
                .SetVaryByQuery("from", "to")
                .Tag(CacheTagPrefix)); // TTL-based for now; true invalidate-on-ingest needs
                                       // a distributed cache backend, not introduced yet.

        return group;
    }

    private static async Task<IResult> GetPricesAsync(
        string zone,
        DateTimeOffset? from,
        DateTimeOffset? to,
        IPriceObservationRepository repository,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (!Enum.TryParse<PriceArea>(zone, ignoreCase: true, out var priceArea))
        {
            return Results.Problem(
                title: "Unknown price zone",
                detail: $"'{zone}' is not one of NO1..NO5.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var today = timeProvider.GetUtcNow().Date;
        var fromUtc = from ?? new DateTimeOffset(today, TimeSpan.Zero);
        var toUtc = to ?? fromUtc.AddDays(2); // today + tomorrow by default

        if (toUtc <= fromUtc)
        {
            return Results.Problem(
                title: "Invalid range",
                detail: "'to' must be after 'from'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var observations = await repository.GetRangeAsync(priceArea, fromUtc, toUtc, ct).ConfigureAwait(false);

        var dto = observations
            .Select(o => new PriceObservationDto(o.Zone.ToString(), o.HourStartUtc, o.PriceExVatOrePerKwh, o.Source))
            .ToList();

        return Results.Ok(dto);
    }
}

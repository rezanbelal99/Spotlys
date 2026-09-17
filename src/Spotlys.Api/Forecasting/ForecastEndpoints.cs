using Microsoft.AspNetCore.OutputCaching;
using Spotlys.Application.Forecasting;
using Spotlys.Domain.Forecasting;
using Spotlys.Domain.Pricing;

namespace Spotlys.Api.Forecasting;

/// <summary>One hour's quantile fan on the wire -- ex VAT, matching docs/DOMAIN.md's storage
/// convention. Never a single price; the whole point of a forecast is the range
/// (CLAUDE.md rule 8).</summary>
internal sealed record QuantileForecastDto(
    DateTimeOffset TargetHourUtc,
    decimal Q05,
    decimal Q25,
    decimal Q50,
    decimal Q75,
    decimal Q95)
{
    public static QuantileForecastDto FromDomain(QuantileForecast forecast) => new(
        forecast.TargetHourUtc, forecast.Q05, forecast.Q25, forecast.Q50, forecast.Q75, forecast.Q95);
}

internal sealed record ForecastFanDto(string Zone, DateTimeOffset IssuedAtUtc, IReadOnlyList<QuantileForecastDto> Hours);

internal static class ForecastEndpoints
{
    private const int DefaultHorizonHours = 168;
    private const int MaxHorizonHours = 168; // FORECASTING.md §1: the model's own horizon

    public static RouteGroupBuilder MapForecastEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/forecast/{zone}", GetForecastAsync)
            .WithName("GetForecast")
            .CacheOutput(policy => policy
                .Expire(TimeSpan.FromMinutes(5))
                .SetVaryByRouteValue("zone")
                .SetVaryByQuery("issuedAt", "horizon")
                .Tag("forecast-"));

        return group;
    }

    private static async Task<IResult> GetForecastAsync(
        string zone,
        DateTimeOffset? issuedAt,
        int? horizon,
        IForecastService forecastService,
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

        var horizonHours = horizon ?? DefaultHorizonHours;
        if (horizonHours is < 1 or > MaxHorizonHours)
        {
            return Results.Problem(
                title: "Invalid horizon",
                detail: $"'horizon' must be between 1 and {MaxHorizonHours} hours.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var issuedAtUtc = issuedAt ?? timeProvider.GetUtcNow();

        var fan = await forecastService.GetForecastAsync(priceArea, issuedAtUtc, horizonHours, ct).ConfigureAwait(false);
        if (fan is null)
        {
            // Not all five quantile models are promoted for this zone/regime yet -- an
            // operational state, not a server error (docs/ARCHITECTURE.md §6: Problem
            // Details for every error, no bare 500s).
            return Results.Problem(
                title: "Forecast unavailable",
                detail: $"No active model for {priceArea} at the regime {issuedAtUtc:O} falls into.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var dto = new ForecastFanDto(
            priceArea.ToString(), issuedAtUtc, fan.Hours.Select(QuantileForecastDto.FromDomain).ToList());

        return Results.Ok(dto);
    }
}

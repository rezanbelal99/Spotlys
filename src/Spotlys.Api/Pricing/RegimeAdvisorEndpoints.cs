using System.Security.Claims;
using Spotlys.Api.Accounts;
using Spotlys.Application.Accounts;
using Spotlys.Application.Common;
using Spotlys.Application.Pricing;

namespace Spotlys.Api.Pricing;

internal sealed record RegimeAdvisorRequestDto(Guid MeterProfileId);

internal sealed record CostRangeDto(decimal LowExVatNok, decimal HighExVatNok, decimal ExpectedExVatNok)
{
    public static CostRangeDto FromDomain(CostRange range) => new(range.LowExVatNok, range.HighExVatNok, range.ExpectedExVatNok);
}

internal sealed record BillDecompositionDto(CostRangeDto EnergyExVatNok, decimal NettleieExVatNok, decimal AvgifterExVatNok)
{
    public static BillDecompositionDto FromDomain(RegimeComparison comparison) => new(
        CostRangeDto.FromDomain(comparison.EnergyExVatNok), comparison.NettleieExVatNok, comparison.AvgifterExVatNok);
}

internal sealed record RegimeAdvisorResponseDto(
    BillDecompositionDto Norgespris,
    BillDecompositionDto SpotWithStromstotte,
    string RecommendedRegime,
    IReadOnlyList<string> ReasoningSentences,
    string ElhubLink)
{
    public static RegimeAdvisorResponseDto FromDomain(RegimeAdvisorResult result) => new(
        BillDecompositionDto.FromDomain(result.Norgespris),
        BillDecompositionDto.FromDomain(result.SpotWithStromstotte),
        result.RecommendedRegime,
        result.ReasoningSentences,
        result.ElhubLink);
}

internal static class RegimeAdvisorEndpoints
{
    public static RouteGroupBuilder MapRegimeAdvisorEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/advisor/regime", GetRegimeAdviceAsync)
            .WithName("RegimeAdvice")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiting.StrictPolicy); // parses a body, runs the
                                                             // tariff engine twice and ONNX
                                                             // inference once -- same cost
                                                             // class as /plan

        return group;
    }

    private static async Task<IResult> GetRegimeAdviceAsync(
        RegimeAdvisorRequestDto request,
        ClaimsPrincipal user,
        IMeterProfileRepository meters,
        RegimeAdvisorUseCase useCase,
        CancellationToken ct)
    {
        var meter = await meters.GetAsync(request.MeterProfileId, ct).ConfigureAwait(false);
        if (meter is null || meter.UserId != CurrentUser.Id(user))
        {
            return Results.Problem(title: "Meter not found", statusCode: StatusCodes.Status404NotFound);
        }

        var result = await useCase.RunAsync(request.MeterProfileId, ct).ConfigureAwait(false);
        if (result is null)
        {
            return Results.Problem(
                title: "Regime advice unavailable",
                detail: "No active forecast model or no grid tariff on file for this meter right now.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(RegimeAdvisorResponseDto.FromDomain(result));
    }
}

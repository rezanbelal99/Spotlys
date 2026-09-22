using System.Security.Claims;
using Spotlys.Api.Accounts;
using Spotlys.Application.Accounts;
using Spotlys.Application.Scheduling;
using Spotlys.Domain.Scheduling;

namespace Spotlys.Api.Scheduling;

internal sealed record FlexibleLoadRequestDto(
    decimal EnergyKwh, decimal MaxPowerKw, decimal MinPowerKw,
    DateTimeOffset NotBefore, DateTimeOffset Deadline, bool Interruptible);

internal sealed record PlanRequestDto(Guid MeterProfileId, FlexibleLoadRequestDto Load);

internal sealed record CostRangeDto(decimal LowExVatNok, decimal HighExVatNok, decimal ExpectedExVatNok)
{
    public static CostRangeDto FromDomain(CostRange range) => new(range.LowExVatNok, range.HighExVatNok, range.ExpectedExVatNok);
}

internal sealed record CounterfactualDto(string Label, CostRangeDto Cost)
{
    public static CounterfactualDto FromDomain(Counterfactual counterfactual) =>
        new(counterfactual.Label, CostRangeDto.FromDomain(counterfactual.Cost));
}

internal sealed record HourAllocationDto(DateTimeOffset HourStartUtc, decimal AllocatedKwh, decimal MarginalCostExVatOrePerKwh)
{
    public static HourAllocationDto FromDomain(HourAllocation allocation) =>
        new(allocation.HourStartUtc, allocation.AllocatedKwh, allocation.MarginalCostExVatOrePerKwh);
}

internal sealed record PlanResponseDto(
    IReadOnlyList<HourAllocationDto> Allocations,
    CostRangeDto ChosenCost,
    CounterfactualDto Counterfactual,
    string BindingConstraint,
    bool IsFullyScheduled)
{
    public static PlanResponseDto FromDomain(PlanResult result) => new(
        result.Allocations.Select(HourAllocationDto.FromDomain).ToList(),
        CostRangeDto.FromDomain(result.ChosenCost),
        CounterfactualDto.FromDomain(result.Counterfactual),
        result.BindingConstraint,
        result.IsFullyScheduled);
}

internal static class PlanEndpoints
{
    public static RouteGroupBuilder MapPlanEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/plan", PlanAsync)
            .WithName("PlanCharge")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiting.StrictPolicy); // parses a body, runs the
                                                             // tariff engine, the
                                                             // optimizer, and ONNX
                                                             // inference -- the most
                                                             // expensive endpoint

        return group;
    }

    private static async Task<IResult> PlanAsync(
        PlanRequestDto request,
        ClaimsPrincipal user,
        IMeterProfileRepository meters,
        PlanChargeUseCase useCase,
        CancellationToken ct)
    {
        if (request.Load.Deadline <= request.Load.NotBefore)
        {
            return Results.Problem(
                title: "Invalid load window", detail: "'deadline' must be after 'notBefore'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var meter = await meters.GetAsync(request.MeterProfileId, ct).ConfigureAwait(false);
        if (meter is null || meter.UserId != CurrentUser.Id(user))
        {
            // 404, not 403 -- see ConsumptionImportEndpoints' own remark on this pattern.
            return Results.Problem(title: "Meter not found", statusCode: StatusCodes.Status404NotFound);
        }

        var load = new FlexibleLoad(
            request.Load.EnergyKwh, request.Load.MaxPowerKw, request.Load.MinPowerKw,
            request.Load.NotBefore, request.Load.Deadline, request.Load.Interruptible);

        var result = await useCase.RunAsync(request.MeterProfileId, load, ct).ConfigureAwait(false);
        if (result is null)
        {
            // The meter/tariff/model checks the use case itself does map to 503, not 500 --
            // the meter was already confirmed to exist and be owned above, so a null result
            // here means no active forecast model or no tariff on file, an operational
            // state (docs/ARCHITECTURE.md §6).
            return Results.Problem(
                title: "Plan unavailable",
                detail: "No active forecast model or no grid tariff on file for this meter right now.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(PlanResponseDto.FromDomain(result));
    }
}

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Spotlys.Api.Accounts;
using Spotlys.Application.Accounts;
using Spotlys.Domain.Pricing;

namespace Spotlys.Api.Metering;

internal sealed record CreateMeterRequestDto(
    string Zone, string GridCompanyId, string SupportScheme, bool IsCabin,
    decimal SupplierMarkupExVatOrePerKwh, decimal SupplierMonthlyFeeExVatNok);

internal sealed record MeterProfileDto(
    Guid Id, string Zone, string GridCompanyId, string SupportScheme, bool IsCabin,
    decimal SupplierMarkupExVatOrePerKwh, decimal SupplierMonthlyFeeExVatNok, DateTimeOffset CreatedAtUtc)
{
    public static MeterProfileDto FromEntry(MeterProfileEntry entry) => new(
        entry.Id, entry.Zone, entry.GridCompanyId, entry.SupportScheme, entry.IsCabin,
        entry.SupplierMarkupExVatOrePerKwh, entry.SupplierMonthlyFeeExVatNok, entry.CreatedAtUtc);
}

internal static class MeterEndpoints
{
    private static readonly string[] ValidSchemes = ["stromstotte", "norgespris"];

    public static RouteGroupBuilder MapMeterEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/meters", CreateAsync)
            .WithName("CreateMeter")
            .RequireAuthorization();

        group.MapGet("/meters", ListAsync)
            .WithName("ListMeters")
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> CreateAsync(
        CreateMeterRequestDto request,
        ClaimsPrincipal user,
        IMeterProfileRepository meters,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (!Enum.TryParse<PriceArea>(request.Zone, ignoreCase: true, out _))
        {
            return Results.Problem(
                title: "Unknown price zone", detail: $"'{request.Zone}' is not one of NO1..NO5.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The canonical (lowercase) form, not request.SupportScheme.ToLowerInvariant() --
        // CA1308 flags invariant lower-casing of untrusted input; matching against the
        // known-good list and using its own value sidesteps that entirely.
        var canonicalScheme = ValidSchemes.FirstOrDefault(s => s.Equals(request.SupportScheme, StringComparison.OrdinalIgnoreCase));
        if (canonicalScheme is null)
        {
            return Results.Problem(
                title: "Unknown support scheme", detail: "Must be 'stromstotte' or 'norgespris'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var entry = new MeterProfileEntry(
            Guid.CreateVersion7(), CurrentUser.Id(user), request.Zone.ToUpperInvariant(),
            request.GridCompanyId, canonicalScheme, request.IsCabin,
            request.SupplierMarkupExVatOrePerKwh, request.SupplierMonthlyFeeExVatNok,
            ConsumptionRetentionYears: 3, timeProvider.GetUtcNow());

        try
        {
            await meters.CreateAsync(entry, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            return Results.Problem(
                title: "Could not create meter", detail: "Unknown grid company.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(MeterProfileDto.FromEntry(entry));
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user, IMeterProfileRepository meters, CancellationToken ct)
    {
        var entries = await meters.ListForUserAsync(CurrentUser.Id(user), ct).ConfigureAwait(false);
        return Results.Ok(entries.Select(MeterProfileDto.FromEntry).ToList());
    }
}

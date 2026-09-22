using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Spotlys.Application.Accounts;
using Spotlys.Application.Metering;
using Spotlys.Infrastructure.Accounts;

namespace Spotlys.Api.Accounts;

internal sealed record DeleteAccountRequestDto(string Password);

internal sealed record ExportedConsumptionDto(DateTimeOffset HourStartUtc, decimal Kwh);

internal sealed record ExportedMeterDto(
    Guid Id, string Zone, string GridCompanyId, string SupportScheme, bool IsCabin, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ExportedConsumptionDto> Consumption);

internal sealed record AccountExportDto(
    Guid UserId, string Email, DateTimeOffset CreatedAtUtc, IReadOnlyList<ExportedMeterDto> Meters, DateTimeOffset ExportedAtUtc);

internal static class GdprEndpoints
{
    public static RouteGroupBuilder MapGdprEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/account/export", ExportAsync)
            .WithName("ExportAccount")
            .RequireAuthorization();

        group.MapDelete("/account", DeleteAsync)
            .WithName("DeleteAccount")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiting.StrictPolicy);

        return group;
    }

    private static async Task<IResult> ExportAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        IMeterProfileRepository meters,
        IConsumptionReadingRepository consumption,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Problem(title: "Account not found", statusCode: StatusCodes.Status404NotFound);
        }

        var meterEntries = await meters.ListForUserAsync(user.Id, ct).ConfigureAwait(false);

        var exportedMeters = new List<ExportedMeterDto>();
        foreach (var meter in meterEntries)
        {
            var readings = await consumption.GetAllAsync(meter.Id, ct).ConfigureAwait(false);
            exportedMeters.Add(new ExportedMeterDto(
                meter.Id, meter.Zone, meter.GridCompanyId, meter.SupportScheme, meter.IsCabin, meter.CreatedAtUtc,
                readings.Select(r => new ExportedConsumptionDto(r.HourStartUtc, r.Kwh)).ToList()));
        }

        var export = new AccountExportDto(
            user.Id, user.Email!, user.CreatedAtUtc, exportedMeters, timeProvider.GetUtcNow());

        return Results.Ok(export);
    }

    private static async Task<IResult> DeleteAsync(
        [FromBody] DeleteAccountRequestDto request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Problem(title: "Account not found", statusCode: StatusCodes.Status404NotFound);
        }

        // Irreversible action -- requires re-confirming the password, not just an active
        // session (CLAUDE.md's spirit applied to account deletion, not just payments).
        var passwordOk = await userManager.CheckPasswordAsync(user, request.Password).ConfigureAwait(false);
        if (!passwordOk)
        {
            return Results.Problem(title: "Incorrect password", statusCode: StatusCodes.Status401Unauthorized);
        }

        await signInManager.SignOutAsync().ConfigureAwait(false);

        // Deletes app_user; meter_profile and metering.consumption_reading cascade via FK
        // (docs/DATA.md §4: export and delete endpoints from day one).
        var result = await userManager.DeleteAsync(user).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.Problem(
                title: "Could not delete account",
                detail: string.Join(" ", result.Errors.Select(e => e.Description)),
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok();
    }
}

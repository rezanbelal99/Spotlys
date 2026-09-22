using Microsoft.AspNetCore.Identity;
using Spotlys.Infrastructure.Accounts;

namespace Spotlys.Api.Accounts;

internal sealed record RegisterRequestDto(string Email, string Password);

internal sealed record LoginRequestDto(string Email, string Password);

internal sealed record AccountSummaryDto(Guid Id, string Email);

internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/account/register", RegisterAsync)
            .WithName("Register")
            .RequireRateLimiting(RateLimiting.StrictPolicy);

        group.MapPost("/account/login", LoginAsync)
            .WithName("Login")
            .RequireRateLimiting(RateLimiting.StrictPolicy);

        group.MapPost("/account/logout", LogoutAsync)
            .WithName("Logout")
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequestDto request,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        var result = await userManager.CreateAsync(user, request.Password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.Problem(
                title: "Could not create account",
                detail: string.Join(" ", result.Errors.Select(e => e.Description)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        await signInManager.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        return Results.Ok(new AccountSummaryDto(user.Id, user.Email!));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequestDto request,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Problem(
                title: "Invalid credentials", statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Results.Problem(
                title: "Invalid credentials", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(new AccountSummaryDto(user.Id, user.Email!));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<AppUser> signInManager, CancellationToken ct)
    {
        await signInManager.SignOutAsync().ConfigureAwait(false);
        return Results.Ok();
    }
}

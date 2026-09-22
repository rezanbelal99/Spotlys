using System.Security.Claims;

namespace Spotlys.Api.Accounts;

/// <summary>Reads the signed-in user's id from the cookie-authenticated
/// <see cref="ClaimsPrincipal"/> -- every meter-scoped endpoint needs this to check
/// ownership. Ownership itself (does this meter belong to this user) is checked per
/// endpoint against the fetched <c>MeterProfileEntry.UserId</c> directly, not through a
/// separate guard abstraction -- the check is a one-line comparison, and this project
/// avoids abstractions beyond what's needed (CLAUDE.md's working agreement).</summary>
internal static class CurrentUser
{
    public static Guid Id(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return raw is not null && Guid.TryParse(raw, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated request has no valid user id claim.");
    }
}

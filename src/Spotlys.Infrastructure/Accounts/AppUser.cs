using Microsoft.AspNetCore.Identity;

namespace Spotlys.Infrastructure.Accounts;

/// <summary>
/// The Spotlys account -- ASP.NET Core Identity's own generated user type
/// (docs/ARCHITECTURE.md §6), extended with the one column ARCHITECTURE.md §4's original
/// <c>app_user</c> sketch named that <see cref="IdentityUser{TKey}"/> doesn't already have.
/// Public, not internal: <c>Spotlys.Api</c>'s auth endpoints use
/// <c>UserManager&lt;AppUser&gt;</c>/<c>SignInManager&lt;AppUser&gt;</c> directly, the same
/// way it already references <c>Spotlys.Domain</c> types.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    /// <summary>When this account was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}

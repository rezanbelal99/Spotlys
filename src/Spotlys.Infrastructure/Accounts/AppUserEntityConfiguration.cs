using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Accounts;

/// <summary>
/// Renames Identity's own <c>AspNetUsers</c> table to this project's snake_case convention.
/// Deliberately does NOT rename <see cref="AppUser"/>'s inherited columns (UserName,
/// PasswordHash, SecurityStamp, ...) -- a narrow, stated exception to snake_case: those
/// columns are addressed exclusively through <c>UserManager&lt;AppUser&gt;</c>/EF, never
/// hand-queried or joined from raw SQL anywhere else in the system, unlike
/// <c>meter_profile.user_id</c>, which the app does query directly and which stays
/// snake_case throughout.
/// </summary>
internal sealed class AppUserEntityConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_user");
        builder.Property(u => u.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
    }
}

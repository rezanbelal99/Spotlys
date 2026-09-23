using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Spotlys.Domain.Pricing;
using Spotlys.Infrastructure.Accounts;
using Spotlys.Infrastructure.Forecasting;
using Spotlys.Infrastructure.Hydrology;
using Spotlys.Infrastructure.Ingestion;
using Spotlys.Infrastructure.Metering;
using Spotlys.Infrastructure.Pricing;
using Spotlys.Infrastructure.Weather;

namespace Spotlys.Infrastructure;

/// <summary>The single EF Core context for Spotlys. Migrations are applied by
/// Spotlys.Migrator only -- never at API startup (docs/ARCHITECTURE.md §4).
/// <see cref="IdentityUserContext{TUser,TKey}"/>, not the full <see cref="IdentityDbContext{TUser}"/>
/// -- no role-based authorization anywhere in this product (docs/ARCHITECTURE.md §6), so
/// AspNetRoles/AspNetUserRoles/AspNetRoleClaims are never created.</summary>
public sealed class SpotlysDbContext(DbContextOptions<SpotlysDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    internal DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();

    internal DbSet<IngestionRunRow> IngestionRuns => Set<IngestionRunRow>();

    internal DbSet<GridCompanyRow> GridCompanies => Set<GridCompanyRow>();

    internal DbSet<SchemeParameterRow> SchemeParameters => Set<SchemeParameterRow>();

    internal DbSet<GridTariffRow> GridTariffs => Set<GridTariffRow>();

    internal DbSet<WeatherPointRow> WeatherPoints => Set<WeatherPointRow>();

    internal DbSet<WeatherForecastRow> WeatherForecasts => Set<WeatherForecastRow>();

    internal DbSet<WeatherFetchCacheRow> WeatherFetchCache => Set<WeatherFetchCacheRow>();

    internal DbSet<HydrologyObservationRow> HydrologyObservations => Set<HydrologyObservationRow>();

    internal DbSet<HydrologyWeekNormRow> HydrologyWeekNorms => Set<HydrologyWeekNormRow>();

    internal DbSet<ModelVersionRow> ModelVersions => Set<ModelVersionRow>();

    internal DbSet<ModelBacktestReportRow> ModelBacktestReports => Set<ModelBacktestReportRow>();

    internal DbSet<MeterProfileRow> MeterProfiles => Set<MeterProfileRow>();

    internal DbSet<ConsumptionReadingRow> ConsumptionReadings => Set<ConsumptionReadingRow>();

    internal DbSet<ConsumptionRetentionPurgeRow> ConsumptionRetentionPurges => Set<ConsumptionRetentionPurgeRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        // Identity's own generic-type tables, renamed to this project's snake_case
        // convention -- their columns keep Identity's own PascalCase (see
        // AppUserEntityConfiguration's remarks; the same reasoning applies here).
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("app_user_claim");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("app_user_login");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("app_user_token");
        // Unused this phase (no passkey/WebAuthn login) -- excluded entirely rather than
        // mapped: its nested IdentityPasskeyData value object isn't fully model-buildable
        // out of the box in this EF Core version without extra configuration this phase has
        // no use for (found live: "IdentityPasskeyData requires a primary key").
        builder.Ignore<IdentityUserPasskey<Guid>>();

        builder.ApplyConfigurationsFromAssembly(typeof(SpotlysDbContext).Assembly);
    }
}

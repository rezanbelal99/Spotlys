using Microsoft.EntityFrameworkCore;
using Spotlys.Domain.Pricing;
using Spotlys.Infrastructure.Forecasting;
using Spotlys.Infrastructure.Hydrology;
using Spotlys.Infrastructure.Ingestion;
using Spotlys.Infrastructure.Pricing;
using Spotlys.Infrastructure.Weather;

namespace Spotlys.Infrastructure;

/// <summary>The single EF Core context for Spotlys. Migrations are applied by
/// Spotlys.Migrator only -- never at API startup (docs/ARCHITECTURE.md §4).</summary>
public sealed class SpotlysDbContext(DbContextOptions<SpotlysDbContext> options) : DbContext(options)
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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SpotlysDbContext).Assembly);
    }
}

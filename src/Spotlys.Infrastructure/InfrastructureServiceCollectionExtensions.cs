using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spotlys.Application.Forecasting;
using Spotlys.Application.Hydrology;
using Spotlys.Application.Ingestion;
using Spotlys.Application.Pricing;
using Spotlys.Application.Weather;
using Spotlys.Infrastructure.Forecasting;
using Spotlys.Infrastructure.Hydrology;
using Spotlys.Infrastructure.Ingestion;
using Spotlys.Infrastructure.Pricing;
using Spotlys.Infrastructure.Weather;

namespace Spotlys.Infrastructure;

/// <summary>Wires up every Infrastructure implementation behind its Application port.
/// Called once from each host (Api, Ingestion, Migrator).</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers the DbContext, repositories, and the hvakosterstrommen HTTP
    /// client with the standard resilience handler (docs/DATA.md §6).</summary>
    public static IServiceCollection AddSpotlysInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Spotlys")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Spotlys.");

        services.AddDbContext<SpotlysDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IPriceObservationRepository, PriceObservationRepository>();
        services.AddScoped<IIngestionRunWriter, IngestionRunRepository>();
        services.AddScoped<IIngestionRunReader, IngestionRunRepository>();
        services.AddScoped<ISchemeParameterRepository, SchemeParameterRepository>();
        services.AddScoped<IGridTariffRepository, GridTariffRepository>();
        services.AddSingleton<IPublicHolidayProvider, NagerDatePublicHolidayProvider>();

        services.AddScoped<IWeatherPointRepository, WeatherPointRepository>();
        services.AddScoped<IWeatherForecastRepository, WeatherForecastRepository>();
        services.AddScoped<IWeatherFetchCacheStore, WeatherFetchCacheRepository>();
        services.AddScoped<IHydrologyRepository, HydrologyRepository>();
        services.AddScoped<IModelVersionRepository, ModelVersionRepository>();
        services.AddSingleton<OnnxSessionCache>();
        services.AddScoped<IForecastService, OnnxForecastService>();

        services.AddHttpClient<IDayAheadPriceSource, HvakosterstrommenPriceSource>(client =>
        {
            client.BaseAddress = new Uri("https://www.hvakosterstrommen.no");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("spotlys/1.0 (+https://spotlys.no)");
        }).AddStandardResilienceHandler();

        services.AddHttpClient<IWeatherForecastSource, MetLocationforecastSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.met.no");
            // docs/DATA.md §2: a generic User-Agent gets blocked; this must identify the
            // app with a real contact address, not a placeholder.
            var userAgent = configuration["MET_USER_AGENT"]
                ?? throw new InvalidOperationException("Missing MET_USER_AGENT.");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).AddStandardResilienceHandler();

        services.AddHttpClient<INveHydrologySource, NveMagasinstatistikkSource>(client =>
        {
            // Trailing slash matters: HttpClient combines a BaseAddress with a relative URI
            // by simple string concatenation on the path, so a leading slash on the request
            // path (see NveMagasinstatistikkSource) would otherwise silently replace this
            // whole path from the host root instead of appending to it.
            client.BaseAddress = new Uri("https://biapi.nve.no/magasinstatistikk/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("spotlys/1.0 (+https://spotlys.no)");
        }).AddStandardResilienceHandler();

        return services;
    }
}

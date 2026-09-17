using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spotlys.Application.Ingestion;
using Spotlys.Application.Pricing;
using Spotlys.Infrastructure.Ingestion;
using Spotlys.Infrastructure.Pricing;

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

        services.AddHttpClient<IDayAheadPriceSource, HvakosterstrommenPriceSource>(client =>
        {
            client.BaseAddress = new Uri("https://www.hvakosterstrommen.no");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("spotlys/1.0 (+https://spotlys.no)");
        }).AddStandardResilienceHandler();

        return services;
    }
}

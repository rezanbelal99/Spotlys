using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Serilog;
using Spotlys.Application.Hydrology;
using Spotlys.Application.Pricing;
using Spotlys.Application.Weather;
using Spotlys.Domain.Pricing;
using Spotlys.Infrastructure;
using Spotlys.Ingestion;
using Spotlys.Ingestion.Hydrology;
using Spotlys.Ingestion.Pricing;
using Spotlys.Ingestion.Weather;

// Two modes:
//   (default) the long-running Worker Service, scheduling IngestDayAheadPricesJob via
//   Quartz's Postgres job store (docs/ARCHITECTURE.md §5).
//   `--backfill zone=NO2 from=2022-09-01 to=2026-09-15` -- a one-shot run on the same host
//   that exits when done (docs/DATA.md §1's backfill job, "manual" trigger in
//   docs/ARCHITECTURE.md §5's job table). No separate project needed: the upsert-on-natural-
//   key repository makes re-running any window safe (docs/DATA.md §6, idempotent).
if (TryParseBackfillArgs(args, out var zone, out var fromDate, out var toDate))
{
    await RunBackfillAsync(zone, fromDate, toDate).ConfigureAwait(false);
    return;
}

await RunWorkerAsync(args).ConfigureAwait(false);
return;

static bool TryParseBackfillArgs(string[] args, out PriceArea zone, out DateOnly fromDate, out DateOnly toDate)
{
    zone = PriceArea.NO2;
    fromDate = default;
    toDate = default;

    if (Array.IndexOf(args, "--backfill") < 0)
    {
        return false;
    }

    var options = args
        .Where(a => a.Contains('=', StringComparison.Ordinal))
        .Select(a => a.Split('=', 2))
        .ToDictionary(kv => kv[0], kv => kv[1], StringComparer.OrdinalIgnoreCase);

    if (options.TryGetValue("zone", out var zoneStr))
    {
        zone = Enum.Parse<PriceArea>(zoneStr, ignoreCase: true);
    }

    if (!options.TryGetValue("from", out var fromStr) || !DateOnly.TryParse(fromStr, out fromDate))
    {
        throw new ArgumentException("--backfill requires from=YYYY-MM-DD");
    }

    toDate = options.TryGetValue("to", out var toStr) && DateOnly.TryParse(toStr, out var parsedTo)
        ? parsedTo
        : DateOnly.FromDateTime(DateTime.UtcNow.Date);

    return true;
}

static async Task RunBackfillAsync(PriceArea zone, DateOnly fromDate, DateOnly toDate)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSpotlysInfrastructure(builder.Configuration);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<IngestDayAheadPricesUseCase>();

    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var useCase = scope.ServiceProvider.GetRequiredService<IngestDayAheadPricesUseCase>();

    Console.WriteLine($"Backfilling {zone} from {fromDate} to {toDate}...");
    var result = await useCase.RunAsync(zone, fromDate, toDate, CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine($"Done: {result.Status}, {result.Rows} rows.");
    if (result.Error is not null)
    {
        Console.WriteLine($"Errors: {result.Error}");
    }
}

static async Task RunWorkerAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog(cfg => cfg
        .ReadFrom.Configuration(builder.Configuration)
        .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

    builder.Services.AddSpotlysInfrastructure(builder.Configuration);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IngestionTelemetry>();
    builder.Services.AddScoped<IngestDayAheadPricesUseCase>();
    builder.Services.AddScoped<IngestDayAheadPricesJob>();
    builder.Services.AddScoped<IngestWeatherForecastUseCase>();
    builder.Services.AddScoped<IngestWeatherForecastJob>();
    builder.Services.AddScoped<IngestHydrologyUseCase>();
    builder.Services.AddScoped<IngestHydrologyJob>();

    var connectionString = builder.Configuration.GetConnectionString("Spotlys")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Spotlys.");
    var osloTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    builder.Services.AddQuartz(q =>
    {
        var jobKey = new JobKey(IngestDayAheadPricesUseCase.JobName);
        q.AddJob<QuartzIngestionJobAdapter<IngestDayAheadPricesJob>>(j => j
            .WithIdentity(jobKey)
            .StoreDurably());

        // docs/ARCHITECTURE.md §5: "12:45, then every 5 min until success, max 14:30".
        q.AddTrigger(t => t
            .ForJob(jobKey)
            .WithIdentity($"{IngestDayAheadPricesUseCase.JobName}-trigger")
            .WithDailyTimeIntervalSchedule(s => s
                .StartingDailyAt(new TimeOnly(12, 45))
                .EndingDailyAt(new TimeOnly(14, 30))
                .WithInterval(5, IntervalUnit.Minute)
                .OnEveryDay()
                .InTimeZone(osloTimeZone)));

        var weatherJobKey = new JobKey(IngestWeatherForecastUseCase.JobName);
        q.AddJob<QuartzIngestionJobAdapter<IngestWeatherForecastJob>>(j => j
            .WithIdentity(weatherJobKey)
            .StoreDurably());

        // docs/ARCHITECTURE.md §5: "04:00, 10:00, 16:00, 22:00".
        q.AddTrigger(t => t
            .ForJob(weatherJobKey)
            .WithIdentity($"{IngestWeatherForecastUseCase.JobName}-trigger")
            .WithDailyTimeIntervalSchedule(s => s
                .StartingDailyAt(new TimeOnly(4, 0))
                .EndingDailyAt(new TimeOnly(22, 0))
                .WithInterval(6, IntervalUnit.Hour)
                .OnEveryDay()
                .InTimeZone(osloTimeZone)));

        var hydrologyJobKey = new JobKey(IngestHydrologyUseCase.JobName);
        q.AddJob<QuartzIngestionJobAdapter<IngestHydrologyJob>>(j => j
            .WithIdentity(hydrologyJobKey)
            .StoreDurably());

        // docs/ARCHITECTURE.md §5: "Wednesdays 09:00".
        q.AddTrigger(t => t
            .ForJob(hydrologyJobKey)
            .WithIdentity($"{IngestHydrologyUseCase.JobName}-trigger")
            .WithDailyTimeIntervalSchedule(s => s
                .StartingDailyAt(new TimeOnly(9, 0))
                .EndingDailyAt(new TimeOnly(9, 0))
                .WithInterval(1, IntervalUnit.Day)
                .OnDaysOfTheWeek(DayOfWeek.Wednesday)
                .InTimeZone(osloTimeZone)));

        // Survives restarts, no double-firing across replicas (docs/ARCHITECTURE.md §5).
        // Schema is created by Spotlys.Migrator (see the AddQuartzSchema EF migration),
        // never auto-provisioned here -- migrations belong in the dedicated init container
        // only (docs/ARCHITECTURE.md §4).
        q.UsePersistentStore(s =>
        {
            s.UseDataSource(ds =>
            {
                ds.Provider = "Npgsql";
                ds.ConnectionString = connectionString;
            });
            s.UseDriverDelegate<Quartz.Impl.AdoJobStore.PostgreSQLDelegate>();
            s.UseSerializer<Quartz.Impl.SystemTextJsonObjectSerializer>();
        });
    });
    builder.Services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);

    using var host = builder.Build();
    await host.RunAsync().ConfigureAwait(false);
}

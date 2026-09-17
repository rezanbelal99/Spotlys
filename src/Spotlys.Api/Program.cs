using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Spotlys.Api;
using Spotlys.Api.Forecasting;
using Spotlys.Api.Pricing;
using Spotlys.Api.Status;
using Spotlys.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(cfg => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddSpotlysInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();

builder.Services.AddOutputCache();

// RFC 9457 Problem Details for every error (docs/ARCHITECTURE.md §6) -- no bare 500s.
builder.Services.AddProblemDetails();

// Baseline limiter for every endpoint, plus a stricter named policy for endpoints that
// parse a request body and run the tariff engine (docs/ARCHITECTURE.md §6).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
            }));

    options.AddFixedWindowLimiter(RateLimiting.StrictPolicy, policy =>
    {
        policy.PermitLimit = 10;
        policy.Window = TimeSpan.FromMinutes(1);
    });
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

builder.Services.AddCors(options =>
{
    // Vite dev server (docs/ARCHITECTURE.md §7). Production origin is added once the real
    // domain exists (docs/DEVOPS.md's deploy scope for this phase stays local).
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseRateLimiter();
app.UseOutputCache();

app.MapPrometheusScrapingEndpoint();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

var api = app.MapGroup("/api/v1");
api.MapPricingEndpoints();
api.MapStatusEndpoints();
api.MapBillEndpoints();
api.MapForecastEndpoints();

app.Run();

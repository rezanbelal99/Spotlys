using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Spotlys.Api;
using Spotlys.Api.Accounts;
using Spotlys.Api.Forecasting;
using Spotlys.Api.Metering;
using Spotlys.Api.Pricing;
using Spotlys.Api.Status;
using Spotlys.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(cfg => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddSpotlysInfrastructure(builder.Configuration);

// Cookie auth, the ARCHITECTURE.md §6 default (BankID/OIDC documented as a deliberate
// deferral -- docs/adr/0009). AddSpotlysAuthentication adds SignInManager/token providers
// on top of AddSpotlysInfrastructure's store-only Identity registration -- only Spotlys.Api
// needs either, so only Spotlys.Api registers them (Spotlys.Migrator/Spotlys.Ingestion
// don't have an authentication scheme or data protection provider to hang them off).
//
// AddIdentityCookies, not a single hand-rolled AddCookie -- found live: SecurityStampValidator
// (which re-validates a cookie against the user's current SecurityStamp, so a deleted
// account's already-issued cookie stops authenticating instead of staying valid until it
// naturally expires) unconditionally references Identity's OTHER standard schemes
// (Identity.TwoFactorRememberMe among them) during its own cleanup, even though nothing
// here uses 2FA -- a single AddCookie("Identity.Application", ...) throws
// "No sign-out authentication handler is registered for the scheme
// 'Identity.TwoFactorRememberMe'" the first time a stamp actually fails validation.
// AddIdentityCookies registers all of Identity's standard schemes (unused ones stay dormant)
// and wires the SecurityStampValidator event automatically, which is why it's the
// documented convenience path rather than assembling cookies by hand.
builder.Services.AddDataProtection();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(cookies => cookies.ApplicationCookie!.Configure(options =>
    {
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // An API, not a page app -- unauthenticated/forbidden requests get a plain status
        // code, never a redirect to a login page that doesn't exist here.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }));
builder.Services.AddAuthorization();
builder.Services.AddSpotlysAuthentication();

// SecurityStampValidator's own default (30 minutes) only re-checks the user store on that
// interval, not every request, so a deleted/locked account's already-issued cookie would
// otherwise keep authenticating for up to half an hour -- tightened to 5 minutes, still far
// cheaper than a per-request DB round trip, for a single-droplet deployment this size
// (docs/DEVOPS.md §11: no scale that would make this a real cost).
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));

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
app.UseAuthentication();
app.UseAuthorization();
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
api.MapAuthEndpoints();
api.MapGdprEndpoints();
api.MapMeterEndpoints();
api.MapConsumptionImportEndpoints();
api.MapPeakEndpoints();

app.Run();

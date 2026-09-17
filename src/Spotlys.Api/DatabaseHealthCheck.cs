using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Spotlys.Infrastructure;

namespace Spotlys.Api;

// A plain SELECT 1, not a dedicated NuGet package (AspNetCore.HealthChecks.NpgSql isn't
// named in the docs and this is a one-line check) -- /health/ready per docs/ARCHITECTURE.md
// §8: "the readiness check including database connectivity and data freshness."
internal sealed class DatabaseHealthCheck(SpotlysDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database is not reachable.", ex);
        }
    }
}

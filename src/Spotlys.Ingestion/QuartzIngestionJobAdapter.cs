using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Spotlys.Ingestion;

/// <summary>
/// The decorator docs/ARCHITECTURE.md §5 describes: every job runs through this, which
/// times it, records the docs/DATA.md §6 telemetry, and logs the outcome. The ingestion_run
/// row itself is written inside the use case (Spotlys.Application), not here -- this layer
/// is Quartz-execution and metrics concerns only, not domain record-keeping.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class QuartzIngestionJobAdapter<TJob>(
    TJob job,
    IngestionTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<QuartzIngestionJobAdapter<TJob>> logger) : IJob
    where TJob : IIngestionJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var asOf = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await job.RunAsync(asOf, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (result is null)
            {
                IngestionLog.NothingToDo(logger, job.Name);
                return;
            }

            var lag = asOf - result.StartedAtUtc;
            telemetry.RecordRun(job.Name, result.Rows ?? 0, stopwatch.Elapsed, lag);
            IngestionLog.JobFinished(logger, job.Name, result.Status, result.Rows, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            IngestionLog.JobThrew(logger, ex, job.Name);
            throw;
        }
    }
}

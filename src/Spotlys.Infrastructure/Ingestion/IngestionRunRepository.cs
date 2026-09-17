using Microsoft.EntityFrameworkCore;
using Spotlys.Application.Ingestion;

namespace Spotlys.Infrastructure.Ingestion;

internal sealed class IngestionRunRepository(SpotlysDbContext dbContext)
    : IIngestionRunWriter, IIngestionRunReader
{
    public async Task RecordAsync(IngestionRunEntry entry, CancellationToken ct)
    {
        dbContext.IngestionRuns.Add(new IngestionRunRow
        {
            JobName = entry.JobName,
            StartedAt = entry.StartedAtUtc,
            FinishedAt = entry.FinishedAtUtc,
            Status = entry.Status.ToString().ToUpperInvariant(),
            Rows = entry.Rows,
            WindowFrom = entry.WindowFromUtc,
            WindowTo = entry.WindowToUtc,
            Error = entry.Error,
        });
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IngestionRunEntry?> GetLatestAsync(string jobName, CancellationToken ct)
    {
        var row = await dbContext.IngestionRuns
            .Where(r => r.JobName == jobName)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new IngestionRunEntry(
            row.JobName,
            row.StartedAt,
            row.FinishedAt,
            Enum.Parse<IngestionRunStatus>(row.Status, ignoreCase: true),
            row.Rows,
            row.WindowFrom,
            row.WindowTo,
            row.Error
        );
    }
}

using Spotlys.Application.Ingestion;
using Spotlys.Application.Pricing;

namespace Spotlys.Api.Status;

/// <summary>One feed's freshness. `IsStale` is what the UI banner reads before showing any
/// price (docs/DEVOPS.md §7: "users are told before they're shown a number").</summary>
internal sealed record FeedStatusDto(
    string JobName,
    DateTimeOffset? LastRunStartedAtUtc,
    string? LastRunStatus,
    int? LastRunRows,
    bool IsStale
);

internal static class StatusEndpoints
{
    // Mirrors IngestDayAheadPricesJob.MaxStaleness in Spotlys.Ingestion, which Api doesn't
    // reference. A small, deliberate duplication rather than a shared "feed config" table
    // this phase doesn't otherwise need -- worth centralizing if a second feed arrives.
    private static readonly TimeSpan DayAheadPricesMaxStaleness = TimeSpan.FromHours(25);

    public static RouteGroupBuilder MapStatusEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/status", GetStatusAsync).WithName("GetStatus");
        return group;
    }

    private static async Task<IResult> GetStatusAsync(
        IIngestionRunReader runReader, TimeProvider timeProvider, CancellationToken ct)
    {
        var latest = await runReader.GetLatestAsync(IngestDayAheadPricesUseCase.JobName, ct).ConfigureAwait(false);

        var isStale = latest is null
            || timeProvider.GetUtcNow() - latest.StartedAtUtc > DayAheadPricesMaxStaleness;

        var dto = new FeedStatusDto(
            IngestDayAheadPricesUseCase.JobName,
            latest?.StartedAtUtc,
            latest?.Status.ToString(),
            latest?.Rows,
            isStale
        );

        return Results.Ok(new[] { dto });
    }
}

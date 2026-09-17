namespace Spotlys.Application.Hydrology;

/// <summary>Outcome of fetching NVE's hydrology data.</summary>
public enum HydrologyFetchStatus
{
    /// <summary>Data was found and parsed.</summary>
    Found,

    /// <summary>A real transport or parse failure.</summary>
    Failed,
}

/// <summary>One fetch outcome. Use <see cref="HydrologyFetchResult"/>'s static factories
/// rather than the constructor.</summary>
public sealed record HydrologyFetchResult<T>(HydrologyFetchStatus Status, IReadOnlyList<T> Entries, string? Error);

/// <summary>Factories for <see cref="HydrologyFetchResult{T}"/> -- a non-generic companion
/// type since CA1000 disallows static members directly on a generic type.</summary>
public static class HydrologyFetchResult
{
    /// <summary>Data was found and parsed.</summary>
    public static HydrologyFetchResult<T> Found<T>(IReadOnlyList<T> entries) =>
        new(HydrologyFetchStatus.Found, entries, null);

    /// <summary>A real transport or parse failure occurred.</summary>
    public static HydrologyFetchResult<T> Failed<T>(string error) =>
        new(HydrologyFetchStatus.Failed, [], error);
}

/// <summary>
/// Port for NVE's magasinstatistikk API (docs/DATA.md §3). No API key -- confirmed live
/// during Phase 3 planning.
/// </summary>
public interface INveHydrologySource
{
    /// <summary>The latest published week's reservoir reading, one row per price zone.</summary>
    public Task<HydrologyFetchResult<HydrologyObservationEntry>> FetchLatestWeekAsync(CancellationToken ct);

    /// <summary>The full min/median/max-per-ISO-week reference distribution, one row per
    /// zone per week.</summary>
    public Task<HydrologyFetchResult<HydrologyWeekNormEntry>> FetchWeekNormsAsync(CancellationToken ct);
}

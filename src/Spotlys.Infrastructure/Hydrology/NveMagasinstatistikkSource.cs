using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Spotlys.Application.Hydrology;

namespace Spotlys.Infrastructure.Hydrology;

// NVE's magasinstatistikk response shape, confirmed live during Phase 3 planning
// (docs/DATA.md §3). No API key. `omrType: "EL"` is the price-area (elspot) breakdown --
// `omrnr` 1..5 map directly to NO1..NO5, confirmed against NVE's own HentOmråder
// endpoint ("NO 1".."NO 5" descriptions).
internal sealed record NveWeekRecord(
    [property: JsonPropertyName("dato_Id")] string DatoId,
    [property: JsonPropertyName("omrType")] string OmrType,
    [property: JsonPropertyName("omrnr")] int Omrnr,
    [property: JsonPropertyName("fyllingsgrad")] float Fyllingsgrad,
    [property: JsonPropertyName("kapasitet_TWh")] float KapasitetTWh,
    [property: JsonPropertyName("fylling_TWh")] float FyllingTWh,
    [property: JsonPropertyName("neste_Publiseringsdato")] DateTime? NestePubliseringsdato);

internal sealed record NveMinMaxMedianRecord(
    [property: JsonPropertyName("omrType")] string OmrType,
    [property: JsonPropertyName("omrnr")] int Omrnr,
    [property: JsonPropertyName("iso_uke")] int IsoUke,
    [property: JsonPropertyName("minFyllingsgrad")] float MinFyllingsgrad,
    [property: JsonPropertyName("medianFyllingsGrad")] float MedianFyllingsgrad,
    [property: JsonPropertyName("maxFyllingsgrad")] float MaxFyllingsgrad);

/// <summary>Named HttpClient for NVE's magasinstatistikk API (registered in
/// InfrastructureServiceCollectionExtensions).</summary>
internal sealed class NveMagasinstatistikkSource(HttpClient httpClient, TimeProvider timeProvider)
    : INveHydrologySource
{
    public const string Source = "nve-magasinstatistikk";
    private const string ElspotAreaType = "EL";

    public async Task<HydrologyFetchResult<HydrologyObservationEntry>> FetchLatestWeekAsync(CancellationToken ct)
    {
        List<NveWeekRecord>? records;
        try
        {
            records = await httpClient
                .GetFromJsonAsync<List<NveWeekRecord>>(
                    "api/Magasinstatistikk/HentOffentligDataSisteUke", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return HydrologyFetchResult.Failed<HydrologyObservationEntry>(ex.Message);
        }

        if (records is null)
        {
            return HydrologyFetchResult.Failed<HydrologyObservationEntry>("empty response body");
        }

        var fetchedAt = timeProvider.GetUtcNow();
        var entries = records
            .Where(r => r.OmrType == ElspotAreaType && r.Omrnr is >= 1 and <= 5)
            .Select(r => new HydrologyObservationEntry(
                $"NO{r.Omrnr}",
                DateOnly.ParseExact(r.DatoId, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                fetchedAt,
                r.Fyllingsgrad,
                r.KapasitetTWh,
                r.FyllingTWh,
                r.NestePubliseringsdato is { } next ? new DateTimeOffset(next, TimeSpan.Zero) : null,
                Source))
            .ToList();

        return HydrologyFetchResult.Found<HydrologyObservationEntry>(entries);
    }

    public async Task<HydrologyFetchResult<HydrologyWeekNormEntry>> FetchWeekNormsAsync(CancellationToken ct)
    {
        List<NveMinMaxMedianRecord>? records;
        try
        {
            records = await httpClient
                .GetFromJsonAsync<List<NveMinMaxMedianRecord>>(
                    "api/Magasinstatistikk/HentOffentligDataMinMaxMedian", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return HydrologyFetchResult.Failed<HydrologyWeekNormEntry>(ex.Message);
        }

        if (records is null)
        {
            return HydrologyFetchResult.Failed<HydrologyWeekNormEntry>("empty response body");
        }

        var fetchedAt = timeProvider.GetUtcNow();
        var entries = records
            .Where(r => r.OmrType == ElspotAreaType && r.Omrnr is >= 1 and <= 5)
            .Select(r => new HydrologyWeekNormEntry(
                $"NO{r.Omrnr}", r.IsoUke, r.MinFyllingsgrad, r.MedianFyllingsgrad, r.MaxFyllingsgrad, fetchedAt))
            .ToList();

        return HydrologyFetchResult.Found<HydrologyWeekNormEntry>(entries);
    }
}

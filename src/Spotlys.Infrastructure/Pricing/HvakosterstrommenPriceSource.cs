using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Infrastructure.Pricing;

// hvakosterstrommen.no's own response shape, confirmed live in spike/fetch_prices.py:
// [{"NOK_per_kWh":0.7532,"EUR_per_kWh":0.06369,"EXR":11.826,
//   "time_start":"2026-01-01T00:00:00+01:00","time_end":"...+01:00"}, ...]
// NOK_per_kWh is kroner, not øre -- docs/DOMAIN.md stores øre/kWh, so this source
// multiplies by 100 on the way out. Prices are excluding VAT (docs/DATA.md §1); no VAT
// math happens here or anywhere upstream of Spotlys.Domain.Pricing.TariffEngine (Phase 2).
internal sealed record HksoRecord(
    [property: JsonPropertyName("NOK_per_kWh")] decimal NokPerKwh,
    [property: JsonPropertyName("EXR")] decimal? Exr,
    [property: JsonPropertyName("time_start")] DateTimeOffset TimeStart
);

// Named HttpClient, registered with Microsoft.Extensions.Http.Resilience's standard
// handler (retry + circuit breaker) in DependencyInjection.cs (docs/DATA.md §6).
internal sealed class HvakosterstrommenPriceSource(HttpClient httpClient, TimeProvider timeProvider)
    : IDayAheadPriceSource
{
    public const string Source = "hkso";

    public async Task<DayAheadFetchResult> FetchDayAsync(PriceArea zone, DateOnly forDate, CancellationToken ct)
    {
        var url = new Uri(
            $"/api/v1/prices/{forDate.Year}/{forDate.Month:D2}-{forDate.Day:D2}_{zone}.json",
            UriKind.Relative);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return DayAheadFetchResult.Failed(ex.Message);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return DayAheadFetchResult.NotYetPublished();
        }

        if (!response.IsSuccessStatusCode)
        {
            return DayAheadFetchResult.Failed($"HTTP {(int)response.StatusCode}");
        }

        List<HksoRecord>? records;
        try
        {
            records = await response.Content.ReadFromJsonAsync<List<HksoRecord>>(ct).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return DayAheadFetchResult.Failed($"parse error: {ex.Message}");
        }

        if (records is null || records.Count == 0)
        {
            return DayAheadFetchResult.NotYetPublished();
        }

        var observedAt = timeProvider.GetUtcNow();
        var observations = records
            .Select(r => new PriceObservation(
                zone,
                r.TimeStart.ToUniversalTime(), // source returns Oslo-local offsets; store UTC always (docs/ARCHITECTURE.md §3)
                Math.Round(r.NokPerKwh * 100m, 4),
                r.Exr,
                Source,
                observedAt))
            .ToList();

        return DayAheadFetchResult.Found(observations);
    }
}

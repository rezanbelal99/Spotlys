using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;
using Spotlys.Infrastructure.Pricing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Spotlys.Integration.Tests.Pricing;

/// <summary>
/// Contract tests against a recorded real response (docs/ENGINEERING.md §2: "Ingestion
/// clients | Contract tests against recorded responses | WireMock.Net + committed fixtures
/// | Upstream shape changes detected, no network in CI"). The fixture is a real
/// hvakosterstrommen.no response captured in spike/fetch_prices.py, not hand-written.
/// Reaches the internal HvakosterstrommenPriceSource via InternalsVisibleTo
/// (src/Spotlys.Infrastructure/AssemblyInfo.cs) rather than widening its visibility just
/// for this test.
/// </summary>
public sealed class HvakosterstrommenPriceSourceContractTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "hvakosterstrommen", name);

    private static HvakosterstrommenPriceSource CreateSource(HttpClient client) =>
        new(client, TimeProvider.System);

    [Fact]
    public async Task Real_recorded_response_parses_into_24_hourly_observations_ex_vat_in_ore_utc()
    {
        var body = await File.ReadAllTextAsync(FixturePath("2026-09-01_NO2.json"));
        _server
            .Given(Request.Create().WithPath("/api/v1/prices/2026/09-01_NO2.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        var result = await source.FetchDayAsync(PriceArea.NO2, new DateOnly(2026, 9, 1), CancellationToken.None);

        Assert.Equal(DayAheadFetchStatus.Found, result.Status);
        Assert.Equal(24, result.Observations.Count);

        var first = result.Observations[0];
        // Fixture's first row: NOK_per_kWh=1.56132 -> 156.132 ore ex-VAT; time_start
        // 2026-09-01T00:00:00+02:00 -> 2026-08-31T22:00:00Z (docs/ARCHITECTURE.md §3: UTC).
        Assert.Equal(156.132m, first.PriceExVatOrePerKwh);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero), first.HourStartUtc);
        Assert.Equal(TimeSpan.Zero, first.HourStartUtc.Offset);
        Assert.Equal("hkso", first.Source);
        Assert.Equal(10.832m, first.CurrencyRate);
    }

    [Fact]
    public async Task Http_404_is_not_yet_published_not_a_failure()
    {
        // docs/DATA.md §1: tomorrow's file appears at the earliest ~13:00 the day before;
        // absence is the normal case, not an error.
        _server
            .Given(Request.Create().WithPath("/api/v1/prices/2099/01-01_NO2.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        var result = await source.FetchDayAsync(PriceArea.NO2, new DateOnly(2099, 1, 1), CancellationToken.None);

        Assert.Equal(DayAheadFetchStatus.NotYetPublished, result.Status);
        Assert.Empty(result.Observations);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Http_500_is_a_failure_not_silently_treated_as_absence()
    {
        _server
            .Given(Request.Create().WithPath("/api/v1/prices/2026/09-02_NO2.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        var result = await source.FetchDayAsync(PriceArea.NO2, new DateOnly(2026, 9, 2), CancellationToken.None);

        Assert.Equal(DayAheadFetchStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }
}

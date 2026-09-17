using Spotlys.Application.Hydrology;
using Spotlys.Infrastructure.Hydrology;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Spotlys.Integration.Tests.Hydrology;

/// <summary>
/// Contract tests against real recorded NVE magasinstatistikk responses (docs/ENGINEERING.md
/// §2). Fixtures were captured live during Phase 3 planning/implementation. Reaches the
/// internal NveMagasinstatistikkSource via InternalsVisibleTo.
/// </summary>
public sealed class NveMagasinstatistikkSourceContractTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "nve", name);

    private static NveMagasinstatistikkSource CreateSource(HttpClient client) =>
        new(client, TimeProvider.System);

    [Fact]
    public async Task Real_recorded_latest_week_response_keeps_only_the_five_elspot_zones()
    {
        var body = await File.ReadAllTextAsync(FixturePath("latest-week.json"));
        _server
            .Given(Request.Create().WithPath("/api/Magasinstatistikk/HentOffentligDataSisteUke").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        var result = await source.FetchLatestWeekAsync(CancellationToken.None);

        Assert.Equal(HydrologyFetchStatus.Found, result.Status);
        // Fixture has 9 rows total (elspot NO1-5 plus national/vassdrag rollups); only the
        // 5 elspot ("EL") rows should survive.
        Assert.Equal(5, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.Matches("^NO[1-5]$", e.Zone));

        var no2 = result.Entries.Single(e => e.Zone == "NO2");
        Assert.Equal(new DateOnly(2026, 9, 13), no2.WeekStartDate);
        Assert.Equal(0.4690387f, no2.FillFraction);
        Assert.Equal(34.04359f, no2.CapacityTwh);
        Assert.Equal(15.967761f, no2.FillTwh);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 13, 0, 0, TimeSpan.Zero), no2.NextPublicationUtc);
        Assert.Equal("nve-magasinstatistikk", no2.Source);
    }

    [Fact]
    public async Task Real_recorded_week_norms_response_gives_min_median_max_per_zone_per_iso_week()
    {
        var body = await File.ReadAllTextAsync(FixturePath("week-norms.json"));
        _server
            .Given(Request.Create().WithPath("/api/Magasinstatistikk/HentOffentligDataMinMaxMedian").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        var result = await source.FetchWeekNormsAsync(CancellationToken.None);

        Assert.Equal(HydrologyFetchStatus.Found, result.Status);
        // Fixture has 477 rows total (elspot NO1-5 plus national/vassdrag rollups); only
        // the "EL" (elspot) rows should survive the filter.
        Assert.Equal(265, result.Entries.Count);

        var no2Week37 = result.Entries.Single(e => e.Zone == "NO2" && e.IsoWeek == 37);
        Assert.Equal(0.50861573f, no2Week37.MinFillFraction);
        Assert.Equal(0.8303803f, no2Week37.MedianFillFraction);
        Assert.Equal(0.94988084f, no2Week37.MaxFillFraction);
    }

    [Fact]
    public async Task Http_500_is_a_failure()
    {
        _server
            .Given(Request.Create().WithPath("/api/Magasinstatistikk/HentOffentligDataSisteUke").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        var result = await source.FetchLatestWeekAsync(CancellationToken.None);

        Assert.Equal(HydrologyFetchStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }
}

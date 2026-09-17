using Spotlys.Application.Weather;
using Spotlys.Infrastructure.Weather;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Spotlys.Integration.Tests.Weather;

/// <summary>Fixed just after the committed fixture's own <c>meta.updated_at</c>
/// (2026-09-17T15:31:43Z) so horizon-filtering assertions are deterministic regardless of
/// when the suite actually runs -- <see cref="TimeProvider.System"/> here would make
/// <see cref="Entries_beyond_the_horizon_are_not_included"/> pass or fail depending on how
/// much real wall-clock time has passed since the fixture was captured (found live: it
/// silently stopped trimming anything once real "now" drifted far enough past the fixture's
/// ~9-day span for a 24h horizon to cover the whole thing). CLAUDE.md's "no DateTime.Now,
/// inject TimeProvider" rule exists for exactly this failure mode.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Trivial in-memory fake -- the real store is Postgres-backed
/// (WeatherFetchCacheRepository), which these contract tests don't need; they only care
/// about MetLocationforecastSource's own request/response handling.</summary>
internal sealed class FakeWeatherFetchCacheStore : IWeatherFetchCacheStore
{
    private readonly Dictionary<string, WeatherFetchCacheEntry> _entries = [];

    public Task<WeatherFetchCacheEntry?> GetAsync(string pointId, CancellationToken ct) =>
        Task.FromResult(_entries.GetValueOrDefault(pointId));

    public Task SetAsync(WeatherFetchCacheEntry entry, CancellationToken ct)
    {
        _entries[entry.PointId] = entry;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Contract tests against a real recorded MET Locationforecast response (docs/ENGINEERING.md
/// §2). The fixture was captured live for Kristiansand during Phase 3 planning/implementation.
/// Reaches the internal MetLocationforecastSource via InternalsVisibleTo.
/// </summary>
public sealed class MetLocationforecastSourceContractTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private static readonly WeatherPointEntry Kristiansand = new("no2-kristiansand", "NO2", "demand", "Kristiansand", 58.1467m, 7.9956m, 15);

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "met", name);

    private static readonly DateTimeOffset FixtureNow = new(2026, 9, 17, 16, 0, 0, TimeSpan.Zero);

    private static MetLocationforecastSource CreateSource(HttpClient client, IWeatherFetchCacheStore? cache = null) =>
        new(client, cache ?? new FakeWeatherFetchCacheStore(), new FixedTimeProvider(FixtureNow));

    [Fact]
    public async Task Real_recorded_response_parses_issued_time_from_meta_not_headers()
    {
        var body = await File.ReadAllTextAsync(FixturePath("kristiansand.json"));
        _server
            .Given(Request.Create().WithPath("/weatherapi/locationforecast/2.0/complete").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        var result = await source.FetchAsync(Kristiansand, horizonHours: 200, CancellationToken.None);

        Assert.Equal(WeatherFetchStatus.Found, result.Status);
        Assert.NotEmpty(result.Entries);

        // Fixture: properties.meta.updated_at = 2026-09-17T15:31:43Z -- distinct from any
        // HTTP header, since MET's WireMock stub here sets no Last-Modified at all.
        Assert.All(result.Entries, e => Assert.Equal(
            new DateTimeOffset(2026, 9, 17, 15, 31, 43, TimeSpan.Zero), e.IssuedAtUtc));

        var first = result.Entries[0];
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 16, 0, 0, TimeSpan.Zero), first.ValidAtUtc);
        Assert.Equal(14.9f, first.TempC);
        // cloud_area_fraction=99.7 (a 0-100 percentage in the source) normalised to 0-1.
        Assert.Equal(0.997f, first.CloudFrac!.Value, 3);
        Assert.Equal(0.0f, first.PrecipMm);
    }

    [Fact]
    public async Task Entries_beyond_the_horizon_are_not_included()
    {
        var body = await File.ReadAllTextAsync(FixturePath("kristiansand.json"));
        _server
            .Given(Request.Create().WithPath("/weatherapi/locationforecast/2.0/complete").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        // The fixture spans ~9 days; a 24h horizon must cut it down sharply.
        var result = await source.FetchAsync(Kristiansand, horizonHours: 24, CancellationToken.None);

        Assert.Equal(WeatherFetchStatus.Found, result.Status);
        Assert.True(result.Entries.Count < 30, $"expected a short horizon to trim the response, got {result.Entries.Count}");
    }

    [Fact]
    public async Task A_fresh_cache_entry_makes_no_network_call()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        // No WireMock stub registered at all -- if the source calls the network, WireMock's
        // default 404 response would surface as a Failed result, not NotModified.
        var cache = new FakeWeatherFetchCacheStore();
        await cache.SetAsync(
            new WeatherFetchCacheEntry(
                Kristiansand.PointId, null, DateTimeOffset.UtcNow.AddMinutes(30), DateTimeOffset.UtcNow),
            CancellationToken.None);

        var source = CreateSource(client, cache);
        var result = await source.FetchAsync(Kristiansand, horizonHours: 168, CancellationToken.None);

        Assert.Equal(WeatherFetchStatus.NotModified, result.Status);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task Http_500_is_a_failure()
    {
        _server
            .Given(Request.Create().WithPath("/weatherapi/locationforecast/2.0/complete").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        using var client = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var source = CreateSource(client);

        var result = await source.FetchAsync(Kristiansand, horizonHours: 168, CancellationToken.None);

        Assert.Equal(WeatherFetchStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }
}

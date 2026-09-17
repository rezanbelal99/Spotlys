using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spotlys.Infrastructure.Forecasting;

namespace Spotlys.Integration.Tests.Forecasting;

/// <summary>
/// The mandatory cross-language fixture test ADR 0002 names: asserts
/// <see cref="InferenceFeatureBuilder"/> produces the exact same feature values as the real
/// Python <c>build_features</c> for a fixed price series and fixed <c>as_of</c> values. The
/// fixture was generated once by running the real Python function (not hand-computed) and
/// committed at <c>Fixtures/forecasting/feature_parity_golden.json</c> -- it embeds the raw
/// synthetic price series' own values, not just its RNG seed, because .NET's <c>Random</c>
/// and NumPy's PCG64 are different algorithms and can never produce the same stream from the
/// same seed. No Testcontainers needed: <see cref="InferenceFeatureBuilder"/> is a pure
/// function of an in-memory price dictionary.
/// </summary>
public sealed class InferenceFeatureParityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "forecasting", "feature_parity_golden.json");

    private static GoldenFile LoadGolden()
    {
        var json = File.ReadAllText(FixturePath());
        return JsonSerializer.Deserialize<GoldenFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("golden fixture failed to deserialize");
    }

    private static Dictionary<DateTimeOffset, decimal> BuildPriceDictionary(PriceSeriesFixture series)
    {
        var start = DateTimeOffset.Parse(series.StartUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var price = new Dictionary<DateTimeOffset, decimal>();
        for (var i = 0; i < series.Hours; i++)
        {
            price[start.AddHours(i)] = (decimal)series.Values[i];
        }

        return price;
    }

    [Theory]
    [InlineData("regime_a", "price_series", "A")]
    [InlineData("regime_b", "price_series", "B")]
    [InlineData("edge_early_history", "price_series", "A")]
    [InlineData("dst_transition", "price_series_dst", "A")]
    public void Feature_values_match_the_real_python_build_features_exactly(string caseName, string seriesName, string regime)
    {
        var golden = LoadGolden();
        var series = seriesName == "price_series" ? golden.PriceSeries : golden.PriceSeriesDst;
        var price = BuildPriceDictionary(series);

        var goldenCase = caseName switch
        {
            "regime_a" => golden.RegimeA,
            "regime_b" => golden.RegimeB,
            "edge_early_history" => golden.EdgeEarlyHistory,
            "dst_transition" => golden.DstTransition,
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };

        var asOfUtc = DateTimeOffset.Parse(goldenCase.AsOfUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var asOfFeatures = InferenceFeatureBuilder.BuildAsOfLevelFeatures(price, asOfUtc, regime);
        var columns = regime == "B" ? InferenceFeatureBuilder.RegimeBColumns : InferenceFeatureBuilder.RegimeAColumns;

        foreach (var row in goldenCase.Rows)
        {
            var targetTimeUtc = DateTimeOffset.Parse(row.TargetTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            var vector = InferenceFeatureBuilder.BuildFeatureVector(asOfFeatures, targetTimeUtc, columns);

            for (var i = 0; i < columns.Count; i++)
            {
                var columnName = columns[i];
                var expected = row.Features.TryGetValue(columnName, out var v) ? v : null;
                var actual = vector[i];

                if (expected is null)
                {
                    Assert.True(
                        double.IsNaN(actual),
                        $"[{caseName}] {columnName} at {targetTimeUtc:O}: python=NaN, csharp={actual}");
                }
                else
                {
                    Assert.False(double.IsNaN(actual), $"[{caseName}] {columnName} at {targetTimeUtc:O}: python={expected}, csharp=NaN");
                    var diff = Math.Abs(actual - expected.Value);
                    // Loose enough to absorb decimal<->double round-tripping through the
                    // price dictionary, tight enough that a real divergence still fails.
                    Assert.True(
                        diff < 1e-6,
                        $"[{caseName}] {columnName} at {targetTimeUtc:O}: python={expected}, csharp={actual}, diff={diff}");
                }
            }
        }
    }

    private sealed class GoldenFile
    {
        [JsonPropertyName("price_series")]
        public required PriceSeriesFixture PriceSeries { get; init; }

        [JsonPropertyName("price_series_dst")]
        public required PriceSeriesFixture PriceSeriesDst { get; init; }

        [JsonPropertyName("regime_a")]
        public required GoldenCase RegimeA { get; init; }

        [JsonPropertyName("regime_b")]
        public required GoldenCase RegimeB { get; init; }

        [JsonPropertyName("edge_early_history")]
        public required GoldenCase EdgeEarlyHistory { get; init; }

        [JsonPropertyName("dst_transition")]
        public required GoldenCase DstTransition { get; init; }
    }

    private sealed class PriceSeriesFixture
    {
        [JsonPropertyName("start_utc")]
        public required string StartUtc { get; init; }

        [JsonPropertyName("hours")]
        public required int Hours { get; init; }

        [JsonPropertyName("values")]
        public required List<double> Values { get; init; }
    }

    private sealed class GoldenCase
    {
        [JsonPropertyName("as_of_utc")]
        public required string AsOfUtc { get; init; }

        [JsonPropertyName("rows")]
        public required List<GoldenRow> Rows { get; init; }
    }

    private sealed class GoldenRow
    {
        [JsonPropertyName("target_time")]
        public required string TargetTime { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; init; } = [];

        public Dictionary<string, double?> Features =>
            Extra
                .Where(kv => kv.Key is not ("as_of" or "lead_hours"))
                .ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.Null ? (double?)null : kv.Value.GetDouble());
    }
}

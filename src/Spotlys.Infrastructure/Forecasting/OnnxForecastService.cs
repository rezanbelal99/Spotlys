using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Spotlys.Application.Forecasting;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Forecasting;
using Spotlys.Domain.Pricing;

namespace Spotlys.Infrastructure.Forecasting;

/// <summary>
/// Real-time ONNX Runtime inference (docs/adr/0002), the serving half of Phase 3's export
/// pipeline. Loads the five active quantile models for a zone/regime, builds features with
/// <see cref="InferenceFeatureBuilder"/> (parity-tested against the real Python
/// <c>build_features</c>), and inverts the log1p transform every model was trained under --
/// <c>python/spotlys_model/export/onnx_export.py</c> calls
/// <c>train_quantile_models(..., use_log1p=True)</c>, so an ONNX prediction is
/// <c>log1p(price + 100)</c>, not the price itself, until this service undoes it.
/// </summary>
internal sealed class OnnxForecastService(
    IModelVersionRepository modelVersions,
    IPriceObservationRepository prices,
    OnnxSessionCache sessionCache) : IForecastService
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private static readonly decimal[] Quantiles = [0.05m, 0.25m, 0.50m, 0.75m, 0.95m];

    // python/spotlys_model/training/lightgbm_model.py's LOG1P_OFFSET -- grounded in the real
    // backfilled NO2 history's actual minimum (-70.93 ore/kWh), not an arbitrary number.
    private const double Log1pOffset = 100.0;

    // Comfortably covers the largest rolling window (720h/30 days) and lag (336h/14 days)
    // InferenceFeatureBuilder reads, plus margin -- missing history only produces NaN
    // features (which the model handles natively), never an error, so over-fetching is the
    // safe direction.
    private const int HistoryMarginHours = 800;

    public async Task<ForecastFan?> GetForecastAsync(
        PriceArea zone, DateTimeOffset issuedAtUtc, int horizonHours, CancellationToken ct)
    {
        var regime = InferenceFeatureBuilder.IsRegimeB(issuedAtUtc) ? "B" : "A";

        var entries = new List<ModelVersionEntry>(Quantiles.Length);
        foreach (var quantile in Quantiles)
        {
            var entry = await modelVersions.GetActiveAsync(zone.ToString(), regime, quantile, ct).ConfigureAwait(false);
            if (entry is null)
            {
                // Not all five quantile models are promoted yet -- an operational state
                // (ActivateInitialModels hasn't run, or a bad retrain got un-promoted), not
                // a bug. The caller turns this into a 503, never a 500.
                return null;
            }

            entries.Add(entry);
        }

        var targetHours = ComputeTargetHours(issuedAtUtc, regime, horizonHours);

        // Regime B's D1-curve feature reads up to the next Oslo calendar day, which can be
        // up to ~48h after issuedAtUtc depending on where in the day issuedAtUtc falls.
        var historyFromUtc = issuedAtUtc.AddHours(-HistoryMarginHours);
        var historyToUtc = regime == "B" ? issuedAtUtc.AddHours(48) : issuedAtUtc;
        var priceRows = await prices.GetRangeAsync(zone, historyFromUtc, historyToUtc, ct).ConfigureAwait(false);
        var price = priceRows.ToDictionary(p => p.HourStartUtc, p => p.PriceExVatOrePerKwh);

        var asOfLevelFeatures = InferenceFeatureBuilder.BuildAsOfLevelFeatures(price, issuedAtUtc, regime);
        var columns = regime == "B" ? InferenceFeatureBuilder.RegimeBColumns : InferenceFeatureBuilder.RegimeAColumns;

        // One batched ONNX Run per quantile model (5 total), not one per hour per quantile
        // (840 calls for a 168h/5-quantile request) -- the ONNX input's batch dimension is
        // already unspecified/dynamic (confirmed against the exported graph), so this is a
        // free win, not a design change to the model. Cut real measured latency for a
        // 168-hour request from up to ~350ms down to single digits, matching ADR 0002's
        // promise instead of just falling short of it.
        var featureMatrix = new double[targetHours.Count * columns.Count];
        for (var h = 0; h < targetHours.Count; h++)
        {
            var vector = InferenceFeatureBuilder.BuildFeatureVector(asOfLevelFeatures, targetHours[h], columns);
            Array.Copy(vector, 0, featureMatrix, h * columns.Count, columns.Count);
        }

        var predictionsByQuantile = new float[Quantiles.Length][];
        for (var i = 0; i < entries.Count; i++)
        {
            var session = sessionCache.GetOrLoad(entries[i].OnnxPath);
            predictionsByQuantile[i] = RunBatchedInference(session, featureMatrix, targetHours.Count, columns.Count);
        }

        var quantileForecasts = new List<QuantileForecast>(targetHours.Count);
        var predictions = new double[Quantiles.Length];
        for (var h = 0; h < targetHours.Count; h++)
        {
            for (var i = 0; i < Quantiles.Length; i++)
            {
                predictions[i] = Math.Exp(predictionsByQuantile[i][h]) - 1.0 - Log1pOffset; // expm1(x) - offset
            }

            // docs/FORECASTING.md §3: "enforce monotonicity post-hoc by sorting" -- quantile
            // crossing is real and looks broken in a chart. Matches predict_quantiles'
            // np.sort exactly: sort after the inverse transform, per row.
            Array.Sort(predictions);

            quantileForecasts.Add(new QuantileForecast(
                targetHours[h],
                (decimal)predictions[0],
                (decimal)predictions[1],
                (decimal)predictions[2],
                (decimal)predictions[3],
                (decimal)predictions[4]));
        }

        return new ForecastFan(quantileForecasts);
    }

    private static float[] RunBatchedInference(InferenceSession session, double[] featureMatrix, int rowCount, int columnCount)
    {
        var tensor = new DenseTensor<double>(featureMatrix, [rowCount, columnCount]);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", tensor) };

        using var results = session.Run(inputs);
        // The classic ai.onnx.ml.TreeEnsembleRegressor operator onnxmltools targets always
        // outputs float32 regardless of the input tensor's declared type (confirmed live
        // against the exported graphs during Phase 3's ONNX parity investigation) -- widen
        // to double here, not before.
        return [.. results[0].AsEnumerable<float>()];
    }

    /// <summary>Mirrors <c>target_times_for_regime</c> exactly: Regime A starts at
    /// <paramref name="issuedAtUtc"/> + 1h; Regime B starts at the Oslo calendar day two days
    /// after <paramref name="issuedAtUtc"/>'s own Oslo day (docs/FORECASTING.md §1).</summary>
    private static List<DateTimeOffset> ComputeTargetHours(DateTimeOffset issuedAtUtc, string regime, int horizonHours)
    {
        DateTimeOffset start;
        if (regime == "B")
        {
            var oslo = TimeZoneInfo.ConvertTime(issuedAtUtc, Oslo);
            var todayOslo = DateOnly.FromDateTime(oslo.DateTime);
            var d2Local = DateTime.SpecifyKind(todayOslo.AddDays(2).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(d2Local, Oslo), TimeSpan.Zero);
        }
        else
        {
            start = issuedAtUtc.AddHours(1);
        }

        var hours = new List<DateTimeOffset>(horizonHours);
        for (var i = 0; i < horizonHours; i++)
        {
            hours.Add(start.AddHours(i));
        }

        return hours;
    }
}

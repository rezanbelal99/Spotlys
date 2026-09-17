namespace Spotlys.Infrastructure.Forecasting;

/// <summary>
/// Replicates <c>python/spotlys_model/features/builder.py</c>'s <c>build_features</c> exactly
/// -- same column set, same numeric semantics -- for real-time inference. This is the single
/// riskiest piece of ONNX serving (docs/adr/0002 already names the risk: "a cross-language
/// fixture test asserts equality for a fixed as_of"); every method here has a matching case
/// in <c>tests/Spotlys.Integration.Tests/Fixtures/forecasting/feature_parity_golden.json</c>,
/// generated once by running the real Python function and committed alongside this file.
///
/// Deliberately a pure function of an in-memory price series, not of
/// <c>IPriceObservationRepository</c> directly -- <c>OnnxForecastService</c> owns fetching
/// the real rows; keeping this class DB-free is what lets the parity test run without
/// Testcontainers.
///
/// The trained models consume only calendar + price-history features (weather/hydrology are
/// declared in the Python manifest but not yet wired into training -- confirmed against
/// <c>REGIME_A_FEATURE_COLUMNS</c>/<c>REGIME_B_FEATURE_COLUMNS</c> in
/// <c>python/spotlys_model/training/lightgbm_model.py</c>), so this builder never needs
/// weather or hydrology data.
/// </summary>
internal static class InferenceFeatureBuilder
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private static readonly int[] RollingWindowsHours = [24, 72, 168, 720];
    private static readonly string[] RollingStats = ["mean", "std", "min", "max"];
    private static readonly int[] LagHours = [24, 48, 168, 336];

    /// <summary>Exact order <c>REGIME_A_FEATURE_COLUMNS</c> is built in -- ONNX input tensors
    /// are positional, so this order is load-bearing, not cosmetic.</summary>
    public static IReadOnlyList<string> RegimeAColumns { get; } = BuildRegimeAColumns();

    /// <summary>Exact order <c>REGIME_B_FEATURE_COLUMNS</c> is built in.</summary>
    public static IReadOnlyList<string> RegimeBColumns { get; } =
        [.. RegimeAColumns, "d1_curve_mean", "d1_curve_min", "d1_curve_max", "d1_curve_std"];

    private static List<string> BuildRegimeAColumns()
    {
        var columns = new List<string> { "hour_sin", "hour_cos", "day_of_week", "is_weekend" };
        columns.AddRange(LagHours.Select(h => $"lag_{h}"));
        foreach (var hours in RollingWindowsHours)
        {
            foreach (var stat in RollingStats)
            {
                columns.Add($"rolling_{stat}_{hours}");
            }
        }

        columns.AddRange(["yesterday_mean", "yesterday_peak_hour", "yesterday_spread"]);
        return columns;
    }

    /// <summary>Calendar features for one target hour -- computed directly from the UTC
    /// timestamp, matching <c>_calendar_features</c> exactly (it does not convert to Oslo
    /// local time before reading hour/day-of-week, even though that looks surprising at
    /// first glance -- <c>target_times</c> arrives already UTC-indexed in the Python code
    /// and nothing in that function converts it).</summary>
    public static IReadOnlyDictionary<string, double> BuildCalendarFeatures(DateTimeOffset targetHourUtc)
    {
        var hourOfDay = targetHourUtc.Hour + targetHourUtc.Minute / 60.0;
        var angle = 2 * Math.PI * hourOfDay / 24.0;

        // .NET DayOfWeek: Sunday=0..Saturday=6. pandas Timestamp.dayofweek: Monday=0..Sunday=6.
        var pythonDayOfWeek = ((int)targetHourUtc.DayOfWeek + 6) % 7;

        return new Dictionary<string, double>
        {
            ["hour_sin"] = Math.Sin(angle),
            ["hour_cos"] = Math.Cos(angle),
            ["day_of_week"] = pythonDayOfWeek,
            ["is_weekend"] = pythonDayOfWeek >= 5 ? 1.0 : 0.0,
        };
    }

    /// <summary>Everything that depends only on <paramref name="asOfUtc"/>, not on any
    /// specific target hour -- lags, rolling stats, yesterday, and (Regime B only) the
    /// cleared D+1 curve. Computed once per as_of and broadcast across every target hour's
    /// feature vector, matching <c>build_features</c>'s own structure.</summary>
    /// <param name="price">The full available price history, keyed by hour-start UTC. Safe
    /// to pass more than <paramref name="asOfUtc"/> needs -- every read here is filtered to
    /// <c>&lt;= asOfUtc</c> internally, except the D+1 curve, which is the one deliberate,
    /// narrowly-scoped exception (see <c>_d1_curve_features</c>'s own remarks).</param>
    /// <param name="asOfUtc">When this forecast is issued.</param>
    /// <param name="regime">"A" or "B" -- gates whether the D+1 curve features are computed.</param>
    public static IReadOnlyDictionary<string, double> BuildAsOfLevelFeatures(
        IReadOnlyDictionary<DateTimeOffset, decimal> price, DateTimeOffset asOfUtc, string regime)
    {
        ArgumentNullException.ThrowIfNull(price);

        var visible = price.Where(kv => kv.Key <= asOfUtc).ToDictionary(kv => kv.Key, kv => kv.Value);
        var result = new Dictionary<string, double>();

        foreach (var lagHours in LagHours)
        {
            var sourceTime = asOfUtc.AddHours(-lagHours);
            result[$"lag_{lagHours}"] = visible.TryGetValue(sourceTime, out var v) ? (double)v : double.NaN;
        }

        foreach (var windowHours in RollingWindowsHours)
        {
            var windowStart = asOfUtc.AddHours(-windowHours);
            var windowValues = visible
                .Where(kv => kv.Key > windowStart)
                .Select(kv => (double)kv.Value)
                .ToList();

            foreach (var (stat, value) in ComputeRollingStats(windowValues))
            {
                result[$"rolling_{stat}_{windowHours}"] = value;
            }
        }

        var oslo = TimeZoneInfo.ConvertTime(asOfUtc, Oslo);
        var todayOslo = DateOnly.FromDateTime(oslo.DateTime);
        var (yesterdayStartUtc, yesterdayEndUtc) = OsloCalendarDayUtcRange(todayOslo.AddDays(-1));
        var yesterday = visible
            .Where(kv => kv.Key >= yesterdayStartUtc && kv.Key < yesterdayEndUtc)
            .OrderBy(kv => kv.Key)
            .ToList();

        if (yesterday.Count == 0)
        {
            result["yesterday_mean"] = double.NaN;
            result["yesterday_peak_hour"] = double.NaN;
            result["yesterday_spread"] = double.NaN;
        }
        else
        {
            var peakEntry = yesterday[0];
            foreach (var entry in yesterday)
            {
                if (entry.Value > peakEntry.Value)
                {
                    peakEntry = entry;
                }
            }

            result["yesterday_mean"] = (double)yesterday.Average(kv => kv.Value);
            result["yesterday_peak_hour"] = TimeZoneInfo.ConvertTime(peakEntry.Key, Oslo).Hour;
            result["yesterday_spread"] = (double)(yesterday.Max(kv => kv.Value) - yesterday.Min(kv => kv.Value));
        }

        if (regime == "B")
        {
            var (d1StartUtc, d1EndUtc) = OsloCalendarDayUtcRange(todayOslo.AddDays(1));
            // Unfiltered `price`, not `visible` -- once the auction clears (docs/DOMAIN.md
            // §2), tomorrow's curve is publicly known even though its hours postdate
            // asOfUtc's clock time. The one deliberate exception to "never read data after
            // as_of" in this builder, scoped exactly as narrowly as the Python source.
            var curve = price.Where(kv => kv.Key >= d1StartUtc && kv.Key < d1EndUtc).Select(kv => (double)kv.Value).ToList();

            if (curve.Count == 0)
            {
                result["d1_curve_mean"] = double.NaN;
                result["d1_curve_min"] = double.NaN;
                result["d1_curve_max"] = double.NaN;
                result["d1_curve_std"] = double.NaN;
            }
            else
            {
                result["d1_curve_mean"] = curve.Average();
                result["d1_curve_min"] = curve.Min();
                result["d1_curve_max"] = curve.Max();
                result["d1_curve_std"] = SampleStandardDeviation(curve);
            }
        }

        return result;
    }

    /// <summary>Builds the ordered feature vector for one target hour, ready for the ONNX
    /// input tensor -- combines the as-of-level features (shared across every target hour
    /// this as_of forecasts) with that hour's own calendar features.</summary>
    public static double[] BuildFeatureVector(
        IReadOnlyDictionary<string, double> asOfLevelFeatures,
        DateTimeOffset targetHourUtc,
        IReadOnlyList<string> columnOrder)
    {
        var calendar = BuildCalendarFeatures(targetHourUtc);
        var vector = new double[columnOrder.Count];
        for (var i = 0; i < columnOrder.Count; i++)
        {
            var name = columnOrder[i];
            vector[i] = calendar.TryGetValue(name, out var calendarValue)
                ? calendarValue
                : asOfLevelFeatures.GetValueOrDefault(name, double.NaN);
        }

        return vector;
    }

    /// <summary>mean/min/max are defined for n&gt;=1 (NaN only when the window is empty);
    /// std needs n&gt;=2 for sample variance (ddof=1, matching pandas' default), NaN
    /// otherwise -- <c>getattr(window, stat)() if len(window) &gt; 0 else np.nan</c> applies
    /// the same "all four NaN when empty" rule the Python source uses, and pandas' own
    /// <c>.std()</c> supplies the n&lt;2 NaN for the non-empty-but-singleton case.</summary>
    private static IEnumerable<(string Stat, double Value)> ComputeRollingStats(List<double> values)
    {
        if (values.Count == 0)
        {
            yield return ("mean", double.NaN);
            yield return ("std", double.NaN);
            yield return ("min", double.NaN);
            yield return ("max", double.NaN);
            yield break;
        }

        yield return ("mean", values.Average());
        yield return ("std", SampleStandardDeviation(values));
        yield return ("min", values.Min());
        yield return ("max", values.Max());
    }

    /// <summary>Sample standard deviation (ddof=1), matching pandas' <c>.std()</c> default.
    /// NaN for fewer than 2 values -- dividing by (n-1)=0 is undefined, not zero.</summary>
    private static double SampleStandardDeviation(List<double> values)
    {
        if (values.Count < 2)
        {
            return double.NaN;
        }

        var mean = values.Average();
        var sumSquaredDeviations = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquaredDeviations / (values.Count - 1));
    }

    /// <summary>The UTC range covering one Oslo calendar day, computed the way .NET's own
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> is designed to be
    /// used -- interpreting an unspecified-kind local wall-clock time in the given zone --
    /// which is DST-correct by construction and doesn't need the manual
    /// strip-tz/naive-arithmetic/relocalize workaround the Python side required for
    /// <c>DateOffset</c> arithmetic (see <c>lightgbm_model.issue_times_in_range</c>'s
    /// docstring for why that workaround was necessary there).</summary>
    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) OsloCalendarDayUtcRange(DateOnly osloDay)
    {
        var startLocal = DateTime.SpecifyKind(osloDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(osloDay.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, Oslo);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, Oslo);
        return (new DateTimeOffset(startUtc, TimeSpan.Zero), new DateTimeOffset(endUtc, TimeSpan.Zero));
    }

    /// <summary>docs/FORECASTING.md §1/§2: Regime B issues at 13:15 Oslo, after the
    /// day-ahead auction clears (~13:00, docs/DOMAIN.md §2).</summary>
    public static bool IsRegimeB(DateTimeOffset asOfUtc) => TimeZoneInfo.ConvertTime(asOfUtc, Oslo).Hour >= 13;
}

namespace Spotlys.Domain.Forecasting;

/// <summary>One hour's predictive distribution, as the LightGBM quantile models produce it
/// (docs/FORECASTING.md §3): five quantile spot-price predictions, ex VAT, already
/// monotonicity-sorted by the training/serving pipeline.</summary>
public sealed record QuantileForecast(
    DateTimeOffset TargetHourUtc,
    decimal Q05,
    decimal Q25,
    decimal Q50,
    decimal Q75,
    decimal Q95);

/// <summary>A forecast issued at one point in time, covering some horizon of hours
/// (docs/FORECASTING.md §1). Pure -- no I/O; <c>Spotlys.Application.Forecasting.IForecastService</c>
/// is what actually produces one.</summary>
public sealed record ForecastFan(IReadOnlyList<QuantileForecast> Hours)
{
    // Weights for turning the 5 known quantile points into one expected value: trapezoidal
    // integration of the piecewise-linear quantile function over p in [0,1], extended flat
    // below p=0.05 and above p=0.95 (docs/FORECASTING.md §8: "don't optimise the median,
    // optimise expected cost over the quantile fan"). Derived once:
    //   E[X] = 0.05*Q05 (flat left) + area(0.05->0.25) + area(0.25->0.50)
    //        + area(0.50->0.75) + area(0.75->0.95) + 0.05*Q95 (flat right)
    // which collapses to these five constant weights (they sum to 1).
    private const decimal Weight05 = 0.15m;
    private const decimal Weight25 = 0.225m;
    private const decimal Weight50 = 0.25m;
    private const decimal Weight75 = 0.225m;
    private const decimal Weight95 = 0.15m;

    /// <summary>The expected spot price per hour, ex VAT -- what feeds
    /// <c>Spotlys.Domain.Scheduling.LoadOptimizer</c>'s marginal-cost input once the caller
    /// applies the meter's <c>SupportScheme</c> on top.</summary>
    public IReadOnlyDictionary<DateTimeOffset, decimal> ExpectedSpotExVatOrePerKwh()
    {
        var result = new Dictionary<DateTimeOffset, decimal>(Hours.Count);
        foreach (var hour in Hours)
        {
            result[hour.TargetHourUtc] =
                Weight05 * hour.Q05 + Weight25 * hour.Q25 + Weight50 * hour.Q50 +
                Weight75 * hour.Q75 + Weight95 * hour.Q95;
        }

        return result;
    }
}

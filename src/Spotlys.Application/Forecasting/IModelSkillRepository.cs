namespace Spotlys.Application.Forecasting;

/// <summary>One lead-time bucket's skill figures from the quantile model's own walk-forward
/// backtest (docs/FORECASTING.md §6) -- Regime A only in this minimal report (the harder,
/// headline regime; Regime B's own figures live in the raw report but aren't surfaced by
/// this endpoint yet -- calibration curves and the oracle-weather gap chart stay deferred
/// too, per the Phase 4 plan's explicit "minimal, not gold-plated" scope for this part).
/// </summary>
public sealed record LeadBucketSkill(
    string LeadBucket,
    decimal MaeOrePerKwh,
    decimal SkillVsB1,
    decimal PinballLoss,
    decimal Coverage50,
    decimal Coverage90);

/// <summary>docs/FORECASTING.md §6's decision-regret metric: what the forecast-driven
/// schedule actually cost, against realised prices, versus two naive strategies -- never
/// against a hypothetical, only ever what really happened in the backtest (docs/DOMAIN.md
/// §7's honesty constraint). <see cref="BacktestDays"/> is the sample size behind the
/// figures, so a small or zero count is visible rather than implied to be robust.</summary>
public sealed record DecisionRegret(
    decimal RegretVsChargeOnArrivalNok,
    decimal RegretVsAlways0200Nok,
    int BacktestDays);

/// <summary>One published backtest run's report for a zone (docs/FORECASTING.md §6:
/// "the backtest emits ... a markdown report per run"; this is that run's structured
/// figures, read back for the public <c>/model</c> page). <see cref="GeneratedAtUtc"/> is
/// the backtest run's own timestamp, not "now" -- this reports backtest skill, not live
/// production accuracy (still deferred: no <c>price_forecast</c> persistence or nightly
/// <c>ScoreProductionForecasts</c> scoring job exists yet).</summary>
public sealed record ModelSkillReport(
    string Zone,
    IReadOnlyList<LeadBucketSkill> ByLeadBucket,
    DecisionRegret Regret,
    DateTimeOffset GeneratedAtUtc);

/// <summary>Read-only access to <c>model_backtest_report</c> (docs/adr/0002: Python writes
/// the report via <c>python/spotlys_model/training/report.py</c>, run from <c>model.yml</c>;
/// nothing in .NET writes through this).</summary>
public interface IModelSkillRepository
{
    /// <summary>The most recently generated report for a zone, or null if no backtest run
    /// has published one yet -- an operational state (model.yml hasn't run since this zone
    /// was added), not an error.</summary>
    public Task<ModelSkillReport?> GetLatestAsync(string zone, CancellationToken ct);
}

namespace Spotlys.Application.Forecasting;

/// <summary>
/// One trained-and-exported quantile model's provenance record (docs/FORECASTING.md §7).
/// Written directly by the Python export pipeline (docs/adr/0002: Python trains, .NET
/// serves) -- this type is the read side only; nothing in .NET writes through this yet.
/// </summary>
/// <param name="Id">Surrogate key.</param>
/// <param name="Zone">The price zone this model was trained for.</param>
/// <param name="Regime">'A' or 'B' (docs/FORECASTING.md §1) -- an addition beyond the
/// schema FORECASTING.md §7 sketches, needed because Regime A and B are structurally
/// different models per zone/quantile, not two rows that would otherwise collide.</param>
/// <param name="Quantile">The quantile this specific booster predicts, 0..1.</param>
/// <param name="TrainedAtUtc">When this model was trained.</param>
/// <param name="TrainDataToUtc">The latest price observation the training data included.</param>
/// <param name="GitSha">The commit the training code was at.</param>
/// <param name="OnnxSha256">SHA-256 of the exported .onnx file, for integrity verification.</param>
/// <param name="OnnxPath">Where the .onnx file actually lives -- a local filesystem path
/// for now; becomes an object-storage URI once that infrastructure exists
/// (docs/DEVOPS.md §4), not a schema change later. An addition beyond FORECASTING.md §7's
/// schema, which names <see cref="OnnxSha256"/> for integrity but never says where to find
/// the file it hashes.</param>
/// <param name="BacktestMaeOrePerKwh">MAE from the walk-forward backtest, if computed.</param>
/// <param name="BacktestSkill">Skill vs B1 from the walk-forward backtest, if computed.</param>
/// <param name="IsActive">Whether this is the model currently served. Promotion is manual
/// in this phase -- see docs/ARCHITECTURE.md §5's gated-promotion job, not yet built.</param>
public sealed record ModelVersionEntry(
    long Id,
    string Zone,
    string Regime,
    decimal Quantile,
    DateTimeOffset TrainedAtUtc,
    DateTimeOffset TrainDataToUtc,
    string GitSha,
    string OnnxSha256,
    string OnnxPath,
    decimal? BacktestMaeOrePerKwh,
    decimal? BacktestSkill,
    bool IsActive
);

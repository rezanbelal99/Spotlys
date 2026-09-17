namespace Spotlys.Infrastructure.Forecasting;

internal sealed class ModelVersionRow
{
    public long Id { get; set; }
    public required string Zone { get; set; }
    public required string Regime { get; set; }
    public decimal Quantile { get; set; }
    public DateTimeOffset TrainedAtUtc { get; set; }
    public DateTimeOffset TrainDataToUtc { get; set; }
    public required string GitSha { get; set; }
    public required string OnnxSha256 { get; set; }
    public required string OnnxPath { get; set; }
    public decimal? BacktestMae { get; set; }
    public decimal? BacktestSkill { get; set; }
    public bool IsActive { get; set; }
}

namespace Spotlys.Infrastructure.Forecasting;

internal sealed class ModelBacktestReportRow
{
    public long Id { get; set; }
    public required string Zone { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }

    // Raw jsonb, parsed by ModelBacktestReportRepository -- the schema is owned by the
    // Python side (python/spotlys_model/training/report.py's build_report_json), matching
    // ADR 0002's "Python trains, .NET serves" split: no shared C# type for the document
    // shape, just a read-time parse of the fields this repository actually needs.
    public required string ReportJson { get; set; }
}

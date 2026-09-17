using System.Diagnostics.Metrics;

namespace Spotlys.Ingestion;

// Metric names exactly as specified in docs/DATA.md §6. Actual export to Prometheus is
// Phase 5 work (docs/ENGINEERING.md §4) -- these Instruments exist and are recorded now so
// nothing at the call sites needs to change once that wiring lands.
internal sealed class IngestionTelemetry : IDisposable
{
    private readonly Meter _meter = new("Spotlys.Ingestion");
    private readonly Counter<int> _rows;
    private readonly Histogram<double> _durationSeconds;
    private readonly Histogram<double> _lagSeconds;

    public IngestionTelemetry()
    {
        _rows = _meter.CreateCounter<int>("spotlys.ingest.rows");
        _durationSeconds = _meter.CreateHistogram<double>("spotlys.ingest.duration", unit: "s");
        _lagSeconds = _meter.CreateHistogram<double>("spotlys.ingest.lag_seconds", unit: "s");
    }

    public void RecordRun(string jobName, int rows, TimeSpan duration, TimeSpan? lag)
    {
        var tags = new KeyValuePair<string, object?>("job", jobName);
        _rows.Add(rows, tags);
        _durationSeconds.Record(duration.TotalSeconds, tags);
        if (lag.HasValue)
        {
            _lagSeconds.Record(lag.Value.TotalSeconds, tags);
        }
    }

    public void Dispose() => _meter.Dispose();
}

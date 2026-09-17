using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;

namespace Spotlys.Infrastructure.Forecasting;

/// <summary>
/// Caches loaded ONNX <see cref="InferenceSession"/> instances by file path. Registered as a
/// singleton deliberately, separate from <see cref="OnnxForecastService"/> itself (which is
/// scoped, matching the scoped repositories it depends on) -- loading a session parses the
/// whole model file, and doing that on every request would defeat the "single-digit
/// milliseconds" inference latency docs/adr/0002 promises. A model_version row's
/// <c>onnx_path</c> only changes on promotion (a rare, manual event this phase), so a
/// process-lifetime cache keyed by path is safe without an invalidation mechanism.
/// </summary>
internal sealed class OnnxSessionCache : IDisposable
{
    private readonly ConcurrentDictionary<string, InferenceSession> _sessions = new();

    public InferenceSession GetOrLoad(string onnxPath) =>
        _sessions.GetOrAdd(onnxPath, path => new InferenceSession(path));

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }
}

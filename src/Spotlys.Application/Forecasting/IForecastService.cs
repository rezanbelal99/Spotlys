using Spotlys.Domain.Forecasting;
using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Forecasting;

/// <summary>Produces a live quantile forecast by running the active ONNX models
/// (docs/adr/0002). The real-time counterpart to Phase 3's offline export pipeline --
/// still deliberately not backed by <c>price_forecast</c> persistence or the nightly
/// <c>ScoreProductionForecasts</c> job (those track production accuracy over time; this
/// port only needs to serve one request).</summary>
public interface IForecastService
{
    /// <summary>The quantile fan for every hour in <c>[issuedAtUtc + 1h, issuedAtUtc + horizonHours]</c>
    /// (Regime A) or the equivalent D+2.. window (Regime B), chosen automatically from
    /// <paramref name="issuedAtUtc"/>'s Oslo-local clock time (docs/DOMAIN.md §2). Returns
    /// null if any of the five quantile models for this zone/regime isn't
    /// <c>is_active</c> yet -- the caller turns that into a 503 Problem Details, never a
    /// 500: an unpromoted model is an operational state, not a bug.</summary>
    public Task<ForecastFan?> GetForecastAsync(
        PriceArea zone, DateTimeOffset issuedAtUtc, int horizonHours, CancellationToken ct);
}

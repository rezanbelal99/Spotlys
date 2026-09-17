namespace Spotlys.Application.Forecasting;

/// <summary>Read-only access to the model_version registry (docs/FORECASTING.md §7).
/// Nothing in .NET writes through this yet -- the Python export pipeline writes rows
/// directly (docs/adr/0002).</summary>
public interface IModelVersionRepository
{
    /// <summary>The currently-active model for a zone/regime/quantile combination, or null
    /// if none has been promoted yet.</summary>
    public Task<ModelVersionEntry?> GetActiveAsync(string zone, string regime, decimal quantile, CancellationToken ct);

    /// <summary>Every model version recorded for a zone, newest first.</summary>
    public Task<IReadOnlyList<ModelVersionEntry>> ListAsync(string zone, CancellationToken ct);
}

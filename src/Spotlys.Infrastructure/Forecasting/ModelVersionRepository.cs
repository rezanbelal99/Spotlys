using Microsoft.EntityFrameworkCore;
using Spotlys.Application.Forecasting;

namespace Spotlys.Infrastructure.Forecasting;

internal sealed class ModelVersionRepository(SpotlysDbContext dbContext) : IModelVersionRepository
{
    public async Task<ModelVersionEntry?> GetActiveAsync(
        string zone, string regime, decimal quantile, CancellationToken ct)
    {
        var row = await dbContext.ModelVersions
            .Where(m => m.Zone == zone && m.Regime == regime && m.Quantile == quantile && m.IsActive)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return row is null ? null : ToEntry(row);
    }

    public async Task<IReadOnlyList<ModelVersionEntry>> ListAsync(string zone, CancellationToken ct)
    {
        var rows = await dbContext.ModelVersions
            .Where(m => m.Zone == zone)
            .OrderByDescending(m => m.TrainedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(ToEntry).ToList();
    }

    private static ModelVersionEntry ToEntry(ModelVersionRow row) => new(
        row.Id,
        row.Zone,
        row.Regime,
        row.Quantile,
        row.TrainedAtUtc,
        row.TrainDataToUtc,
        row.GitSha,
        row.OnnxSha256,
        row.OnnxPath,
        row.BacktestMae,
        row.BacktestSkill,
        row.IsActive);
}

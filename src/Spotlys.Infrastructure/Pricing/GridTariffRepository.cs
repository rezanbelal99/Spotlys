using Microsoft.EntityFrameworkCore;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Infrastructure.Pricing;

internal sealed class GridTariffRepository(SpotlysDbContext dbContext) : IGridTariffRepository
{
    public async Task<GridTariff?> GetAsync(string gridCompanyId, DateOnly date, CancellationToken ct)
    {
        var row = await dbContext.GridTariffs
            .Where(g => g.GridCompanyId == gridCompanyId && g.ValidFrom <= date)
            .OrderByDescending(g => g.ValidFrom)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new GridTariff(
            row.GridCompanyId,
            row.EnergyDayOre,
            row.EnergyNightOre,
            row.CapacitySteps
                .Select(s => new CapacityStep(s.FromKw, s.ToKw, s.MonthlyExVatNok))
                .ToList());
    }

    public async Task<IReadOnlyList<GridCompanySummary>> ListGridCompaniesAsync(CancellationToken ct) =>
        await dbContext.GridCompanies
            .OrderBy(g => g.Name)
            .Select(g => new GridCompanySummary(g.Id, g.Name))
            .ToListAsync(ct)
            .ConfigureAwait(false);
}

using Microsoft.EntityFrameworkCore;
using Spotlys.Application.Accounts;

namespace Spotlys.Infrastructure.Accounts;

internal sealed class MeterProfileRepository(SpotlysDbContext dbContext) : IMeterProfileRepository
{
    public async Task CreateAsync(MeterProfileEntry meter, CancellationToken ct)
    {
        dbContext.MeterProfiles.Add(new MeterProfileRow
        {
            Id = meter.Id,
            UserId = meter.UserId,
            Zone = meter.Zone,
            GridCompanyId = meter.GridCompanyId,
            SupportScheme = meter.SupportScheme,
            IsCabin = meter.IsCabin,
            SupplierMarkupExVatOrePerKwh = meter.SupplierMarkupExVatOrePerKwh,
            SupplierMonthlyFeeExVatNok = meter.SupplierMonthlyFeeExVatNok,
            ConsumptionRetentionYears = meter.ConsumptionRetentionYears,
            CreatedAtUtc = meter.CreatedAtUtc,
        });

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<MeterProfileEntry?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await dbContext.MeterProfiles.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            .ConfigureAwait(false);

        return row is null ? null : ToEntry(row);
    }

    public async Task<IReadOnlyList<MeterProfileEntry>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        var rows = await dbContext.MeterProfiles.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(ToEntry).ToList();
    }

    public async Task<IReadOnlyList<MeterProfileEntry>> ListAllAsync(CancellationToken ct)
    {
        var rows = await dbContext.MeterProfiles.AsNoTracking()
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(ToEntry).ToList();
    }

    private static MeterProfileEntry ToEntry(MeterProfileRow row) => new(
        row.Id, row.UserId, row.Zone, row.GridCompanyId, row.SupportScheme, row.IsCabin,
        row.SupplierMarkupExVatOrePerKwh, row.SupplierMonthlyFeeExVatNok, row.ConsumptionRetentionYears, row.CreatedAtUtc);
}

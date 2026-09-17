using Microsoft.EntityFrameworkCore;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Infrastructure.Pricing;

internal sealed class SchemeParameterRepository(SpotlysDbContext dbContext) : ISchemeParameterRepository
{
    public async Task<StromstotteParameters> GetStromstotteParametersAsync(DateOnly date, CancellationToken ct)
    {
        var threshold = await GetValueAsync("stromstotte", "threshold_ex_vat_ore", date, ct).ConfigureAwait(false);
        var supportRate = await GetValueAsync("stromstotte", "support_rate", date, ct).ConfigureAwait(false);
        return new StromstotteParameters(threshold, supportRate);
    }

    public async Task<NorgesprisParameters> GetNorgesprisParametersAsync(DateOnly date, bool isCabin, CancellationToken ct)
    {
        var rate = await GetValueAsync("norgespris", "rate_ex_vat_ore", date, ct).ConfigureAwait(false);
        var capParameter = isCabin ? "cap_kwh_month_cabin" : "cap_kwh_month_home";
        var cap = await GetValueAsync("norgespris", capParameter, date, ct).ConfigureAwait(false);
        return new NorgesprisParameters(rate, cap);
    }

    public async Task<LevyParameters> GetLevyParametersAsync(DateOnly date, PriceArea zone, CancellationToken ct)
    {
        var forbruksavgift = await GetValueAsync("forbruksavgift", "rate_ex_vat_ore_per_kwh", date, ct).ConfigureAwait(false);
        var enova = await GetValueAsync("enova", "rate_ex_vat_ore_per_kwh", date, ct).ConfigureAwait(false);
        // docs/DOMAIN.md §1: NO4 pays no VAT on electricity -- a function of the delivery
        // zone, never a constant.
        var vatParameter = zone == PriceArea.NO4 ? "no4_rate" : "standard_rate";
        var vatRate = await GetValueAsync("vat", vatParameter, date, ct).ConfigureAwait(false);
        return new LevyParameters(forbruksavgift, enova, vatRate);
    }

    private async Task<decimal> GetValueAsync(string scheme, string parameter, DateOnly date, CancellationToken ct)
    {
        var row = await dbContext.SchemeParameters
            .Where(p => p.Scheme == scheme
                && p.Parameter == parameter
                && p.ValidFrom <= date
                && (p.ValidTo == null || p.ValidTo >= date))
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new InvalidOperationException(
                $"No scheme_parameter row for {scheme}/{parameter} effective {date:O}.");
        }

        return row.Value;
    }
}

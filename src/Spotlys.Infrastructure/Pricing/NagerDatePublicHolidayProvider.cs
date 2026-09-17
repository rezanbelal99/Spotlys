using Nager.Date;
using Spotlys.Application.Pricing;

namespace Spotlys.Infrastructure.Pricing;

// Pinned to Nager.Date 1.30.0 specifically -- see the comment on its PackageVersion entry
// in Directory.Packages.props for why (every newer release requires a paid license key).
internal sealed class NagerDatePublicHolidayProvider : IPublicHolidayProvider
{
    public IReadOnlySet<DateOnly> GetHolidays(int year) =>
        DateSystem.GetPublicHolidays(year, CountryCode.NO)
            .Select(h => DateOnly.FromDateTime(h.Date))
            .ToHashSet();
}

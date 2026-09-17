namespace Spotlys.Application.Pricing;

/// <summary>
/// Port for the Norwegian public holiday calendar (docs/DATA.md §5: "do not hand-roll
/// this"). <see cref="Domain.Pricing.GridTariff"/> stays pure by taking the resolved set as
/// a plain parameter instead of depending on this interface directly.
/// </summary>
public interface IPublicHolidayProvider
{
    /// <summary>All Norwegian public holidays (including movable feasts) in the given year.</summary>
    public IReadOnlySet<DateOnly> GetHolidays(int year);
}

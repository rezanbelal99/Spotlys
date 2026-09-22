using Spotlys.Domain.Pricing;

namespace Spotlys.Domain.Metering;

/// <summary>Small helper for the one thing <see cref="CapProjection"/> needs computed from
/// raw hourly readings -- not a heavyweight type, just "average kWh/day over the trailing
/// window ending at asOf".</summary>
public static class ConsumptionSeries
{
    /// <summary>0 if there's no consumption in the window -- <see cref="CapProjection.ProjectedCrossingDate"/>
    /// already treats that as "never projects a crossing", not an error.</summary>
    public static decimal TrailingDailyAverageKwh(
        IReadOnlyList<HourlyConsumption> readings, DateTimeOffset asOfUtc, int trailingDays)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var windowStart = asOfUtc.AddDays(-trailingDays);
        var totalKwh = readings
            .Where(r => r.HourStartUtc > windowStart && r.HourStartUtc <= asOfUtc)
            .Sum(r => r.Kwh);

        return trailingDays <= 0 ? 0m : totalKwh / trailingDays;
    }
}

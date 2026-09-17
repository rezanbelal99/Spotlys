namespace Spotlys.Domain.Pricing;

/// <summary>
/// CLAUDE.md rule 1's named type: every number a user sees goes through here. Pure --
/// no DB access, no ambient time, no hard-coded rates (every parameter is resolved by the
/// caller from the versioned tables). Implements docs/DOMAIN.md §5's bill formula exactly.
/// </summary>
public static class TariffEngine
{
    /// <summary>
    /// Computes one calendar month's itemised bill. Consumption and spot prices are walked
    /// in a single chronological pass, threading the running month-to-date kWh total
    /// through both the support scheme (for Norgespris's cap) and the capacity-step
    /// calculation.
    /// </summary>
    /// <exception cref="InvalidOperationException">A consumption hour has no matching spot
    /// price -- the engine never invents one.</exception>
    public static Bill CalculateBill(
        SupportScheme scheme,
        GridTariff gridTariff,
        LevyParameters levies,
        decimal supplierMarkupExVatOrePerKwh,
        decimal supplierMonthlyFeeExVatNok,
        IReadOnlyList<HourlyConsumption> monthConsumption,
        IReadOnlyDictionary<DateTimeOffset, decimal> spotPricesExVatOrePerKwh,
        IReadOnlySet<DateOnly> publicHolidaysOslo)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(gridTariff);
        ArgumentNullException.ThrowIfNull(levies);
        ArgumentNullException.ThrowIfNull(monthConsumption);
        ArgumentNullException.ThrowIfNull(spotPricesExVatOrePerKwh);
        ArgumentNullException.ThrowIfNull(publicHolidaysOslo);

        var sortedConsumption = monthConsumption.OrderBy(c => c.HourStartUtc).ToList();
        var hourlyLines = new List<HourlyBillLine>(sortedConsumption.Count);

        var monthToDateKwh = 0m;
        var energyExVatOre = 0m;
        var energileddExVatOre = 0m;
        var forbruksavgiftExVatOre = 0m;
        var enovaExVatOre = 0m;

        foreach (var consumption in sortedConsumption)
        {
            if (!spotPricesExVatOrePerKwh.TryGetValue(consumption.HourStartUtc, out var spot))
            {
                throw new InvalidOperationException(
                    $"No spot price for {consumption.HourStartUtc:O} -- the engine never invents one.");
            }

            var energyRateExVatOrePerKwh = scheme.EnergyCostExVatOrePerKwh(
                spot, supplierMarkupExVatOrePerKwh, consumption.Kwh, monthToDateKwh);
            var isNight = GridTariff.IsNight(consumption.HourStartUtc, publicHolidaysOslo);
            var energileddRateExVatOrePerKwh = isNight
                ? gridTariff.EnergyNightExVatOrePerKwh
                : gridTariff.EnergyDayExVatOrePerKwh;

            var hourEnergyExVatOre = energyRateExVatOrePerKwh * consumption.Kwh;
            var hourEnergileddExVatOre = energileddRateExVatOrePerKwh * consumption.Kwh;

            energyExVatOre += hourEnergyExVatOre;
            energileddExVatOre += hourEnergileddExVatOre;
            forbruksavgiftExVatOre += levies.ForbruksavgiftExVatOrePerKwh * consumption.Kwh;
            enovaExVatOre += levies.EnovaExVatOrePerKwh * consumption.Kwh;

            hourlyLines.Add(new HourlyBillLine(
                consumption.HourStartUtc, spot, consumption.Kwh,
                hourEnergyExVatOre, hourEnergileddExVatOre, isNight));

            monthToDateKwh += consumption.Kwh;
        }

        var topThreeAverageKw = GridTariff.CalculateTopThreeAverageKw(sortedConsumption);
        var kapasitetsleddExVatNok = gridTariff.CapacityStepMonthlyExVatNok(topThreeAverageKw);

        const decimal OreToNok = 100m;
        var energyExVatNok = energyExVatOre / OreToNok;
        var energileddExVatNok = energileddExVatOre / OreToNok;
        var forbruksavgiftExVatNok = forbruksavgiftExVatOre / OreToNok;
        var enovaExVatNok = enovaExVatOre / OreToNok;

        var totalExVatNok = energyExVatNok
            + energileddExVatNok
            + kapasitetsleddExVatNok
            + forbruksavgiftExVatNok
            + enovaExVatNok
            + supplierMonthlyFeeExVatNok;

        var totalIncVatNok = totalExVatNok * (1 + levies.VatRate);

        return new Bill(
            energyExVatNok,
            energileddExVatNok,
            kapasitetsleddExVatNok,
            forbruksavgiftExVatNok,
            enovaExVatNok,
            supplierMonthlyFeeExVatNok,
            levies.VatRate,
            totalExVatNok,
            totalIncVatNok,
            topThreeAverageKw,
            hourlyLines);
    }
}

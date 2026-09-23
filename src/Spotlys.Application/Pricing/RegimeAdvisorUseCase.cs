using Spotlys.Application.Accounts;
using Spotlys.Application.Common;
using Spotlys.Application.Forecasting;
using Spotlys.Application.Metering;
using Spotlys.Domain.Forecasting;
using Spotlys.Domain.Metering;
using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Pricing;

/// <summary>One regime's projected bill, decomposed docs/DOMAIN.md §5-style: the
/// forecast-derived energy line as a range (never a bare number -- CLAUDE.md rule 8), and
/// the two lines docs/DOMAIN.md §4 says are "completely unaffected by Norgespris" as plain
/// figures, since neither depends on which regime wins.</summary>
public sealed record RegimeComparison(CostRange EnergyExVatNok, decimal NettleieExVatNok, decimal AvgifterExVatNok)
{
    /// <summary>The three lines summed at the energy line's expected (not tail) value --
    /// what <see cref="RegimeAdvisorUseCase"/> compares the two regimes on.</summary>
    public decimal ExpectedTotalExVatNok => EnergyExVatNok.ExpectedExVatNok + NettleieExVatNok + AvgifterExVatNok;
}

/// <summary>docs/DOMAIN.md §6: "the regime advisor's job is to place the user in one of
/// these rows and then shut up about the irrelevant advice." <paramref name="RecommendedRegime"/>
/// is structural ("norgespris" | "spot_stromstotte"); <paramref name="ReasoningSentences"/>
/// carries the qualitative "why", per docs/DOMAIN.md §7's honesty constraint that the
/// advisor states its assumptions inline -- never a number invented in prose that isn't
/// also in a structured field (CLAUDE.md rule 8 applies to sentences too, not just DTOs).</summary>
public sealed record RegimeAdvisorResult(
    RegimeComparison Norgespris,
    RegimeComparison SpotWithStromstotte,
    string RecommendedRegime,
    IReadOnlyList<string> ReasoningSentences,
    string ElhubLink);

/// <summary>
/// docs/DOMAIN.md §6's regime advisor: runs the same household (real month-to-date
/// consumption, a trailing-average baseline, and the live forecast fan) through both
/// support schemes and says which costs less. Deliberately projects over the forecast's own
/// horizon (docs/DOMAIN.md §7: "current forward view" is one of the three assumptions the
/// advisor states inline), not a full realised month -- FORECASTING.md has no >7-8 day
/// horizon to project a whole month's spot exposure from. The two schemes never differ on
/// nettleie or avgifter (docs/DOMAIN.md §4/§4c), so those lines are computed once and shared.
/// </summary>
public sealed class RegimeAdvisorUseCase(
    IMeterProfileRepository meters,
    IGridTariffRepository gridTariffs,
    ISchemeParameterRepository schemeParameters,
    IForecastService forecastService,
    IConsumptionReadingRepository consumption,
    IPublicHolidayProvider holidayProvider,
    TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    // Same trailing-average simplification as PlanChargeUseCase -- there's no per-hour
    // consumption forecast in this system, so both schemes are compared against the same
    // flat baseline draw rather than an hour-of-day curve. Consistent, not new imprecision.
    private const int BaselineTrailingDays = 30;

    // docs/DOMAIN.md §7's "current forward view" assumption -- the forecast's own horizon,
    // not a full month. Named in the reasoning sentences so the projection period is never
    // silently implied to be a full monthly bill.
    private const int HorizonDays = 7;

    // The official Norwegian metering-data hub households use to actually change scheme
    // (docs/DOMAIN.md §3b: "ordered by the customer via Elhub"; §7: "links to Elhub for the
    // actual decision").
    private const string ElhubUrl = "https://www.elhub.no/";

    /// <summary>Null if the meter doesn't exist, has no tariff on file, or no active
    /// forecast model exists for its zone/regime -- same null-means-503 contract as
    /// <c>PlanChargeUseCase</c>. Ownership is checked by the caller.</summary>
    public async Task<RegimeAdvisorResult?> RunAsync(Guid meterProfileId, CancellationToken ct)
    {
        var meter = await meters.GetAsync(meterProfileId, ct).ConfigureAwait(false);
        if (meter is null)
        {
            return null;
        }

        if (!Enum.TryParse<PriceArea>(meter.Zone, ignoreCase: true, out var zone))
        {
            return null;
        }

        var nowUtc = timeProvider.GetUtcNow();
        var forDateOslo = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, Oslo).Date);

        var gridTariff = await gridTariffs.GetAsync(meter.GridCompanyId, forDateOslo, ct).ConfigureAwait(false);
        if (gridTariff is null)
        {
            return null;
        }

        var stromstotte = await schemeParameters.GetStromstotteParametersAsync(forDateOslo, ct).ConfigureAwait(false);
        var norgespris = await schemeParameters.GetNorgesprisParametersAsync(forDateOslo, meter.IsCabin, ct).ConfigureAwait(false);
        var levies = await schemeParameters.GetLevyParametersAsync(forDateOslo, zone, ct).ConfigureAwait(false);

        var horizonHours = Math.Min(HorizonDays * 24, 168);
        var fan = await forecastService.GetForecastAsync(zone, nowUtc, horizonHours, ct).ConfigureAwait(false);
        if (fan is null)
        {
            return null;
        }

        var monthStartOslo = new DateOnly(forDateOslo.Year, forDateOslo.Month, 1);
        var monthStartLocal = DateTime.SpecifyKind(monthStartOslo.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var monthStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, Oslo), TimeSpan.Zero);
        var monthToDateReadings = await consumption.GetRangeAsync(meterProfileId, monthStartUtc, nowUtc, ct).ConfigureAwait(false);
        var monthToDateKwh = monthToDateReadings.Sum(r => r.Kwh);
        var peakSummary = PeakTracker.Summarize(monthToDateReadings, gridTariff.CapacitySteps);

        var trailingReadings = await consumption.GetRangeAsync(meterProfileId, nowUtc.AddDays(-BaselineTrailingDays), nowUtc, ct).ConfigureAwait(false);
        var baselineKwhPerHour = trailingReadings.Count > 0 ? trailingReadings.Average(r => r.Kwh) : 0m;
        var trailingDailyAverageKwh = baselineKwhPerHour * 24m;

        var years = fan.Hours.Select(h => TimeZoneInfo.ConvertTime(h.TargetHourUtc, Oslo).Year).Distinct();
        var holidays = years.SelectMany(holidayProvider.GetHolidays).ToHashSet();

        // Energiledd and avgifter don't depend on which regime wins -- computed once, in
        // Oslo-local calendar order so the running month-to-date kWh (needed only for
        // Norgespris's cap test below) resets correctly if the horizon crosses a month end.
        var orderedHours = fan.Hours.OrderBy(h => h.TargetHourUtc).ToList();
        var expectedSpotByHour = fan.ExpectedSpotExVatOrePerKwh();
        var energileddExVatOre = 0m;
        var avgifterExVatOre = 0m;
        var runningMonthToDateKwh = monthToDateKwh;
        var runningMonth = forDateOslo.Month;

        var norgesprisEnergyLow = 0m;
        var norgesprisEnergyHigh = 0m;
        var norgesprisEnergyExpected = 0m;
        var stromstotteEnergyLow = 0m;
        var stromstotteEnergyHigh = 0m;
        var stromstotteEnergyExpected = 0m;

        var norgesprisScheme = new NorgesprisScheme(norgespris);
        var stromstotteScheme = new StromstotteScheme(stromstotte);

        foreach (var hour in orderedHours)
        {
            var localMonth = TimeZoneInfo.ConvertTime(hour.TargetHourUtc, Oslo).Month;
            if (localMonth != runningMonth)
            {
                runningMonthToDateKwh = 0m;
                runningMonth = localMonth;
            }

            var kwh = baselineKwhPerHour;

            var expectedSpot = expectedSpotByHour[hour.TargetHourUtc];
            norgesprisEnergyExpected += norgesprisScheme.EnergyCostExVatOrePerKwh(expectedSpot, meter.SupplierMarkupExVatOrePerKwh, kwh, runningMonthToDateKwh) * kwh;
            norgesprisEnergyLow += norgesprisScheme.EnergyCostExVatOrePerKwh(hour.Q05, meter.SupplierMarkupExVatOrePerKwh, kwh, runningMonthToDateKwh) * kwh;
            norgesprisEnergyHigh += norgesprisScheme.EnergyCostExVatOrePerKwh(hour.Q95, meter.SupplierMarkupExVatOrePerKwh, kwh, runningMonthToDateKwh) * kwh;

            stromstotteEnergyExpected += stromstotteScheme.EnergyCostExVatOrePerKwh(expectedSpot, meter.SupplierMarkupExVatOrePerKwh, kwh, runningMonthToDateKwh) * kwh;
            stromstotteEnergyLow += stromstotteScheme.EnergyCostExVatOrePerKwh(hour.Q05, meter.SupplierMarkupExVatOrePerKwh, kwh, runningMonthToDateKwh) * kwh;
            stromstotteEnergyHigh += stromstotteScheme.EnergyCostExVatOrePerKwh(hour.Q95, meter.SupplierMarkupExVatOrePerKwh, kwh, runningMonthToDateKwh) * kwh;

            energileddExVatOre += gridTariff.EnergileddRateExVatOrePerKwh(hour.TargetHourUtc, holidays) * kwh;
            avgifterExVatOre += (levies.ForbruksavgiftExVatOrePerKwh + levies.EnovaExVatOrePerKwh) * kwh;

            runningMonthToDateKwh += kwh;
        }

        // Kapasitetsledd is a monthly fixed charge, priced against the peak kW the
        // household has *already* set this month (docs/DOMAIN.md §4a) -- it doesn't scale
        // with the forecast horizon's length at all, so pro-rating it by horizon/month-days
        // rather than reporting the full monthly figure keeps the nettleie line on the same
        // "projected week" footing as the energy line above, stated explicitly in the
        // reasoning sentences so it isn't mistaken for a full month's nettleie.
        var daysInMonth = DateTime.DaysInMonth(forDateOslo.Year, forDateOslo.Month);
        var kapasitetsleddExVatNok = gridTariff.CapacityStepMonthlyExVatNok(peakSummary.AverageKw) * HorizonDays / daysInMonth;

        const decimal OreToNok = 100m;
        var nettleieExVatNok = energileddExVatOre / OreToNok + kapasitetsleddExVatNok;
        var avgifterExVatNok = avgifterExVatOre / OreToNok;

        var norgesprisComparison = new RegimeComparison(
            new CostRange(Math.Min(norgesprisEnergyLow, norgesprisEnergyHigh) / OreToNok, Math.Max(norgesprisEnergyLow, norgesprisEnergyHigh) / OreToNok, norgesprisEnergyExpected / OreToNok),
            nettleieExVatNok,
            avgifterExVatNok);

        var stromstotteComparison = new RegimeComparison(
            new CostRange(Math.Min(stromstotteEnergyLow, stromstotteEnergyHigh) / OreToNok, Math.Max(stromstotteEnergyLow, stromstotteEnergyHigh) / OreToNok, stromstotteEnergyExpected / OreToNok),
            nettleieExVatNok,
            avgifterExVatNok);

        var recommended = norgesprisComparison.ExpectedTotalExVatNok <= stromstotteComparison.ExpectedTotalExVatNok
            ? "norgespris"
            : "spot_stromstotte";

        var monthEndOslo = new DateOnly(forDateOslo.Year, forDateOslo.Month, daysInMonth);
        var projectedCrossing = CapProjection.ProjectedCrossingDate(
            monthToDateKwh, forDateOslo, monthEndOslo, trailingDailyAverageKwh, norgespris.CapKwhPerMonth);

        var reasoning = BuildReasoningSentences(zone, meter.IsCabin, stromstotte, norgespris, projectedCrossing, recommended);

        return new RegimeAdvisorResult(norgesprisComparison, stromstotteComparison, recommended, reasoning, ElhubUrl);
    }

    /// <summary>docs/DOMAIN.md §7: state assumptions inline, then place the household in one
    /// of §6's segments rather than giving generic advice. Numbers that also live in the
    /// structured <see cref="RegimeComparison"/> fields aren't repeated here -- the sentences
    /// stay qualitative, matching CLAUDE.md rule 8's spirit applied to prose.</summary>
    private static List<string> BuildReasoningSentences(
        PriceArea zone,
        bool isCabin,
        StromstotteParameters stromstotte,
        NorgesprisParameters norgespris,
        DateOnly? projectedCapCrossing,
        string recommended)
    {
        var sentences = new List<string>
        {
            $"Vurderingen bruker husstandens gjennomsnittlige forbruk de siste {BaselineTrailingDays} dagene og prisprognosen for de neste {HorizonDays} dagene i {zone}.",
            $"Norgespris er i dag {norgespris.RateExVatOrePerKwh:0.#} øre/kWh eks. mva., med et tak på {norgespris.CapKwhPerMonth:0} kWh/måned. " +
            $"Strømstøtten dekker 90 % av prisen over {stromstotte.ThresholdExVatOrePerKwh:0.#} øre/kWh eks. mva.",
        };

        if (isCabin)
        {
            sentences.Add("Som hytte gjelder et lavere Norgespris-tak (1000 kWh/måned) -- vurder frostsikringsbehovet i tillegg til strømregningen.");
        }

        if (zone == PriceArea.NO4)
        {
            sentences.Add("I NO4 betales det ikke mva. på strøm, og prisene ligger strukturelt lavt -- spotpris med strømstøtte lønner seg oftest her.");
        }

        if (projectedCapCrossing is { } crossing)
        {
            sentences.Add($"Med dagens forbruksmønster ser husstanden ut til å passere Norgespris-taket rundt {crossing:d. MMMM}. Forbruk utover taket får ingen støtte i det hele tatt.");
        }

        sentences.Add(recommended == "norgespris"
            ? "Basert på denne prognosen kommer Norgespris best ut for denne husstanden akkurat nå."
            : "Basert på denne prognosen kommer spotpris med strømstøtte best ut for denne husstanden akkurat nå.");

        return sentences;
    }
}

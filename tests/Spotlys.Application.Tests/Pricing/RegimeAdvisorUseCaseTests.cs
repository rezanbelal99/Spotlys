using NSubstitute;
using Spotlys.Application.Accounts;
using Spotlys.Application.Forecasting;
using Spotlys.Application.Metering;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Forecasting;
using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Tests.Pricing;

public class RegimeAdvisorUseCaseTests
{
    private static readonly Guid MeterId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 4, 0, 0, TimeSpan.Zero);

    private static MeterProfileEntry Meter(string zone = "NO2", bool isCabin = false) => new(
        MeterId, UserId, zone, "glitre-nett", "stromstotte", isCabin,
        SupplierMarkupExVatOrePerKwh: 0m, SupplierMonthlyFeeExVatNok: 0m, ConsumptionRetentionYears: 3,
        CreatedAtUtc: Now.AddDays(-60));

    private static GridTariff Tariff() => new(
        "glitre-nett", EnergyDayExVatOrePerKwh: 30m, EnergyNightExVatOrePerKwh: 20m,
        CapacitySteps: [new CapacityStep(0m, 5m, 150m), new CapacityStep(5m, 999999m, 300m)]);

    private static ForecastFan Fan(decimal spotExVatOrePerKwh)
    {
        var hours = Enumerable.Range(1, 168)
            .Select(h => new QuantileForecast(
                Now.AddHours(h), spotExVatOrePerKwh * 0.5m, spotExVatOrePerKwh * 0.8m,
                spotExVatOrePerKwh, spotExVatOrePerKwh * 1.2m, spotExVatOrePerKwh * 1.5m))
            .ToList();
        return new ForecastFan(hours);
    }

    private static RegimeAdvisorUseCase BuildUseCase(
        decimal spotExVatOrePerKwh, string zone = "NO2", bool isCabin = false,
        IReadOnlyList<HourlyConsumption>? trailingConsumption = null)
    {
        var meters = Substitute.For<IMeterProfileRepository>();
        meters.GetAsync(MeterId, Arg.Any<CancellationToken>()).Returns(Meter(zone, isCabin));

        var gridTariffs = Substitute.For<IGridTariffRepository>();
        gridTariffs.GetAsync("glitre-nett", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Tariff());

        var schemeParameters = Substitute.For<ISchemeParameterRepository>();
        schemeParameters.GetStromstotteParametersAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new StromstotteParameters(ThresholdExVatOrePerKwh: 77m, SupportRate: 0.9m));
        schemeParameters.GetNorgesprisParametersAsync(Arg.Any<DateOnly>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new NorgesprisParameters(RateExVatOrePerKwh: 40m, CapKwhPerMonth: callInfo.ArgAt<bool>(1) ? 1000m : 5000m));
        schemeParameters.GetLevyParametersAsync(Arg.Any<DateOnly>(), Arg.Any<PriceArea>(), Arg.Any<CancellationToken>())
            .Returns(new LevyParameters(ForbruksavgiftExVatOrePerKwh: 9.5m, EnovaExVatOrePerKwh: 1m, VatRate: 0.25m));

        var forecastService = Substitute.For<IForecastService>();
        forecastService.GetForecastAsync(Arg.Any<PriceArea>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Fan(spotExVatOrePerKwh));

        // A flat 2 kWh/hour trailing baseline by default, matching PlanChargeUseCaseTests'
        // own style -- without any consumption on file the baseline collapses to zero and
        // both regimes' energy lines are trivially 0 ore, which is a real edge case
        // (Norgespris_meter's-own zero-spread test elsewhere covers that) but not what
        // these price-comparison tests are exercising.
        var defaultTrailingConsumption = Enumerable.Range(0, 24 * 30)
            .Select(h => new HourlyConsumption(Now.AddDays(-30).AddHours(h), Kwh: 2m))
            .ToList();
        var consumption = Substitute.For<IConsumptionReadingRepository>();
        consumption.GetRangeAsync(MeterId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(trailingConsumption ?? defaultTrailingConsumption);

        var holidayProvider = Substitute.For<IPublicHolidayProvider>();
        holidayProvider.GetHolidays(Arg.Any<int>()).Returns(new HashSet<DateOnly>());

        return new RegimeAdvisorUseCase(
            meters, gridTariffs, schemeParameters, forecastService, consumption, holidayProvider, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Recommends_stromstotte_when_the_forecast_is_cheap_and_well_under_the_norgespris_rate()
    {
        // 20 ore/kWh spot is far below both the 77-ore stromstotte threshold and the
        // 40-ore Norgespris flat rate, so spot + stromstotte should win outright.
        var useCase = BuildUseCase(spotExVatOrePerKwh: 20m);

        var result = await useCase.RunAsync(MeterId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("spot_stromstotte", result!.RecommendedRegime);
        Assert.True(result.SpotWithStromstotte.ExpectedTotalExVatNok < result.Norgespris.ExpectedTotalExVatNok);
        // Range + counterfactual-shaped result -- never a bare number (CLAUDE.md rule 8).
        Assert.True(result.SpotWithStromstotte.EnergyExVatNok.LowExVatNok <= result.SpotWithStromstotte.EnergyExVatNok.ExpectedExVatNok);
        Assert.True(result.SpotWithStromstotte.EnergyExVatNok.ExpectedExVatNok <= result.SpotWithStromstotte.EnergyExVatNok.HighExVatNok);
        Assert.NotEmpty(result.ReasoningSentences);
        Assert.Equal("https://www.elhub.no/", result.ElhubLink);
    }

    [Fact]
    public async Task Recommends_norgespris_when_the_forecast_is_expensive()
    {
        // 300 ore/kWh spot is far above the 77-ore threshold -- even with 90% support above
        // it, the marginal cost stays above the flat 40-ore Norgespris rate.
        var useCase = BuildUseCase(spotExVatOrePerKwh: 300m);

        var result = await useCase.RunAsync(MeterId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("norgespris", result!.RecommendedRegime);
        Assert.True(result.Norgespris.ExpectedTotalExVatNok < result.SpotWithStromstotte.ExpectedTotalExVatNok);
    }

    [Fact]
    public async Task Nettleie_and_avgifter_are_identical_across_both_regimes()
    {
        // docs/DOMAIN.md §4/§4c: nettleie and levies are "completely unaffected by
        // Norgespris" -- the two regimes must never differ on these lines.
        var useCase = BuildUseCase(spotExVatOrePerKwh: 90m);

        var result = await useCase.RunAsync(MeterId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(result!.Norgespris.NettleieExVatNok, result.SpotWithStromstotte.NettleieExVatNok);
        Assert.Equal(result.Norgespris.AvgifterExVatNok, result.SpotWithStromstotte.AvgifterExVatNok);
    }

    [Fact]
    public async Task Flags_an_approaching_norgespris_cap_crossing_in_the_reasoning()
    {
        var heavyConsumption = Enumerable.Range(0, 24 * 10)
            .Select(h => new HourlyConsumption(Now.AddDays(-10).AddHours(h), Kwh: 30m))
            .ToList();
        var useCase = BuildUseCase(spotExVatOrePerKwh: 90m, trailingConsumption: heavyConsumption);

        var result = await useCase.RunAsync(MeterId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result!.ReasoningSentences, s => s.Contains("Norgespris-taket", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_meter_returns_null()
    {
        var meters = Substitute.For<IMeterProfileRepository>();
        meters.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MeterProfileEntry?)null);

        var useCase = new RegimeAdvisorUseCase(
            meters,
            Substitute.For<IGridTariffRepository>(),
            Substitute.For<ISchemeParameterRepository>(),
            Substitute.For<IForecastService>(),
            Substitute.For<IConsumptionReadingRepository>(),
            Substitute.For<IPublicHolidayProvider>(),
            new FixedTimeProvider(Now));

        var result = await useCase.RunAsync(MeterId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task No_active_forecast_model_returns_null()
    {
        var meters = Substitute.For<IMeterProfileRepository>();
        meters.GetAsync(MeterId, Arg.Any<CancellationToken>()).Returns(Meter());
        var gridTariffs = Substitute.For<IGridTariffRepository>();
        gridTariffs.GetAsync("glitre-nett", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Tariff());
        var schemeParameters = Substitute.For<ISchemeParameterRepository>();
        schemeParameters.GetStromstotteParametersAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new StromstotteParameters(77m, 0.9m));
        schemeParameters.GetNorgesprisParametersAsync(Arg.Any<DateOnly>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new NorgesprisParameters(40m, 5000m));
        schemeParameters.GetLevyParametersAsync(Arg.Any<DateOnly>(), Arg.Any<PriceArea>(), Arg.Any<CancellationToken>())
            .Returns(new LevyParameters(9.5m, 1m, 0.25m));
        var forecastService = Substitute.For<IForecastService>();
        forecastService.GetForecastAsync(Arg.Any<PriceArea>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((ForecastFan?)null);
        var consumption = Substitute.For<IConsumptionReadingRepository>();
        consumption.GetRangeAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var holidayProvider = Substitute.For<IPublicHolidayProvider>();
        holidayProvider.GetHolidays(Arg.Any<int>()).Returns(new HashSet<DateOnly>());

        var useCase = new RegimeAdvisorUseCase(
            meters, gridTariffs, schemeParameters, forecastService, consumption, holidayProvider, new FixedTimeProvider(Now));

        var result = await useCase.RunAsync(MeterId, CancellationToken.None);

        Assert.Null(result);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

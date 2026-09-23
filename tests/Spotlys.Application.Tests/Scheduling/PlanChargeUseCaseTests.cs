using NSubstitute;
using Spotlys.Application.Accounts;
using Spotlys.Application.Forecasting;
using Spotlys.Application.Metering;
using Spotlys.Application.Pricing;
using Spotlys.Application.Scheduling;
using Spotlys.Domain.Forecasting;
using Spotlys.Domain.Pricing;
using Spotlys.Domain.Scheduling;

namespace Spotlys.Application.Tests.Scheduling;

public class PlanChargeUseCaseTests
{
    private static readonly Guid MeterId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 4, 0, 0, TimeSpan.Zero);

    private static MeterProfileEntry Meter(string scheme = "stromstotte") => new(
        MeterId, UserId, "NO2", "glitre-nett", scheme, IsCabin: false,
        SupplierMarkupExVatOrePerKwh: 0m, SupplierMonthlyFeeExVatNok: 0m, ConsumptionRetentionYears: 3,
        CreatedAtUtc: Now.AddDays(-30));

    private static GridTariff Tariff() => new(
        "glitre-nett", EnergyDayExVatOrePerKwh: 30m, EnergyNightExVatOrePerKwh: 20m,
        CapacitySteps:
        [
            new CapacityStep(0m, 5m, 150m),
            new CapacityStep(5m, 10m, 300m),
        ]);

    private static ForecastFan Fan()
    {
        // Hour 1 (05:00) is cheap, hour 2 (06:00) is expensive, hour 3 (07:00) is mid --
        // the optimizer should prefer hour 1 first.
        var hours = new List<QuantileForecast>
        {
            new(Now.AddHours(1), 20m, 40m, 50m, 60m, 80m),
            new(Now.AddHours(2), 150m, 180m, 200m, 220m, 260m),
            new(Now.AddHours(3), 60m, 90m, 100m, 110m, 140m),
        };
        return new ForecastFan(hours);
    }

    private static PlanChargeUseCase BuildUseCase(
        out IMeterProfileRepository meters,
        out IConsumptionReadingRepository consumption,
        string scheme = "stromstotte")
    {
        meters = Substitute.For<IMeterProfileRepository>();
        meters.GetAsync(MeterId, Arg.Any<CancellationToken>()).Returns(Meter(scheme));

        var gridTariffs = Substitute.For<IGridTariffRepository>();
        gridTariffs.GetAsync("glitre-nett", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Tariff());

        var schemeParameters = Substitute.For<ISchemeParameterRepository>();
        schemeParameters.GetStromstotteParametersAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new StromstotteParameters(ThresholdExVatOrePerKwh: 77m, SupportRate: 0.9m));
        schemeParameters.GetNorgesprisParametersAsync(Arg.Any<DateOnly>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new NorgesprisParameters(RateExVatOrePerKwh: 40m, CapKwhPerMonth: 5000m));

        var forecastService = Substitute.For<IForecastService>();
        forecastService.GetForecastAsync(PriceArea.NO2, Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Fan());

        consumption = Substitute.For<IConsumptionReadingRepository>();
        consumption.GetRangeAsync(MeterId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var timeProvider = new FixedTimeProvider(Now);

        return new PlanChargeUseCase(meters, gridTariffs, schemeParameters, forecastService, consumption, timeProvider);
    }

    [Fact]
    public async Task Chooses_the_cheapest_available_hours_and_reports_a_range_plus_counterfactual()
    {
        var useCase = BuildUseCase(out _, out _);
        var load = new FlexibleLoad(
            EnergyKwh: 2m, MaxPowerKw: 2m, MinPowerKw: 0m, NotBefore: Now, Deadline: Now.AddHours(4), Interruptible: true);

        var result = await useCase.RunAsync(MeterId, load, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsFullyScheduled);
        Assert.Equal(2m, result.Allocations.Sum(a => a.AllocatedKwh));

        // The cheapest hour (05:00, q50=50) should be fully used before the pricier ones.
        var cheapHour = result.Allocations.SingleOrDefault(a => a.HourStartUtc == Now.AddHours(1));
        Assert.NotNull(cheapHour);
        Assert.Equal(2m, cheapHour!.AllocatedKwh);

        // Range + counterfactual, structurally present -- never a bare number.
        Assert.True(result.ChosenCost.LowExVatNok <= result.ChosenCost.ExpectedExVatNok);
        Assert.True(result.ChosenCost.ExpectedExVatNok <= result.ChosenCost.HighExVatNok);
        Assert.Equal("plugger inn nå i stedet", result.Counterfactual.Label);
        Assert.True(result.Counterfactual.Cost.ExpectedExVatNok >= 0m);

        // The optimized (cheapest-first) schedule must never cost more than the naive one --
        // same invariant FORECASTING.md §8 requires of the pure optimizer, now end to end.
        Assert.True(result.ChosenCost.ExpectedExVatNok <= result.Counterfactual.Cost.ExpectedExVatNok + 0.01m);
    }

    [Fact]
    public async Task Norgespris_meter_uses_the_flat_rate_not_the_forecast_spread()
    {
        var useCase = BuildUseCase(out _, out _, scheme: "norgespris");
        var load = new FlexibleLoad(
            EnergyKwh: 2m, MaxPowerKw: 2m, MinPowerKw: 0m, NotBefore: Now, Deadline: Now.AddHours(4), Interruptible: true);

        var result = await useCase.RunAsync(MeterId, load, CancellationToken.None);

        Assert.NotNull(result);
        // Norgespris is a flat rate -- 40 ore/kWh -- regardless of which hour is chosen, so
        // the range should collapse to (almost) nothing: no forecast-driven spread survives
        // a flat-rate scheme, unlike stromstotte's own test above.
        var spread = result!.ChosenCost.HighExVatNok - result.ChosenCost.LowExVatNok;
        Assert.True(spread < 0.01m, $"expected a near-zero spread under a flat rate, got {spread}");
    }

    [Fact]
    public async Task Unknown_meter_returns_null()
    {
        var meters = Substitute.For<IMeterProfileRepository>();
        meters.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MeterProfileEntry?)null);

        var useCase = new PlanChargeUseCase(
            meters,
            Substitute.For<IGridTariffRepository>(),
            Substitute.For<ISchemeParameterRepository>(),
            Substitute.For<IForecastService>(),
            Substitute.For<IConsumptionReadingRepository>(),
            new FixedTimeProvider(Now));

        var load = new FlexibleLoad(1m, 1m, 0m, Now, Now.AddHours(2), true);
        var result = await useCase.RunAsync(MeterId, load, CancellationToken.None);

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
        var forecastService = Substitute.For<IForecastService>();
        forecastService.GetForecastAsync(Arg.Any<PriceArea>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((ForecastFan?)null);
        var consumption = Substitute.For<IConsumptionReadingRepository>();
        consumption.GetRangeAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var useCase = new PlanChargeUseCase(meters, gridTariffs, schemeParameters, forecastService, consumption, new FixedTimeProvider(Now));
        var load = new FlexibleLoad(1m, 1m, 0m, Now, Now.AddHours(2), true);

        var result = await useCase.RunAsync(MeterId, load, CancellationToken.None);

        Assert.Null(result);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

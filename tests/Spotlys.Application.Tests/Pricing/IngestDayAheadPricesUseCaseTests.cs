using NSubstitute;
using Spotlys.Application.Ingestion;
using Spotlys.Application.Pricing;
using Spotlys.Domain.Pricing;

namespace Spotlys.Application.Tests.Pricing;

public class IngestDayAheadPricesUseCaseTests
{
    private static PriceObservation Observation(DateOnly date) =>
        new(
            PriceArea.NO2,
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            100m,
            11.5m,
            "hkso",
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task All_days_found_upserts_everything_and_records_ok()
    {
        var day1 = new DateOnly(2026, 1, 1);
        var day2 = new DateOnly(2026, 1, 2);
        var source = Substitute.For<IDayAheadPriceSource>();
        source.FetchDayAsync(PriceArea.NO2, day1, Arg.Any<CancellationToken>())
            .Returns(DayAheadFetchResult.Found([Observation(day1)]));
        source.FetchDayAsync(PriceArea.NO2, day2, Arg.Any<CancellationToken>())
            .Returns(DayAheadFetchResult.Found([Observation(day2)]));

        var repository = Substitute.For<IPriceObservationRepository>();
        var runWriter = Substitute.For<IIngestionRunWriter>();
        var useCase = new IngestDayAheadPricesUseCase(source, repository, runWriter, TimeProvider.System);

        var result = await useCase.RunAsync(PriceArea.NO2, day1, day2, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.Ok, result.Status);
        Assert.Equal(2, result.Rows);
        Assert.Null(result.Error);
        await repository.Received(1).UpsertRangeAsync(
            Arg.Is<IReadOnlyList<PriceObservation>>(list => list.Count == 2), Arg.Any<CancellationToken>());
        await runWriter.Received(1).RecordAsync(
            Arg.Is<IngestionRunEntry>(e => e.Status == IngestionRunStatus.Ok), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task One_failed_day_among_successes_is_partial_but_still_upserts_the_good_data()
    {
        // docs/DATA.md §6: "honest on failure... a partial fetch is recorded as partial,
        // never silently padded" -- and critically, never silently dropped either. One bad
        // day must not withhold the days that succeeded.
        var goodDay = new DateOnly(2026, 1, 1);
        var badDay = new DateOnly(2026, 1, 2);
        var source = Substitute.For<IDayAheadPriceSource>();
        source.FetchDayAsync(PriceArea.NO2, goodDay, Arg.Any<CancellationToken>())
            .Returns(DayAheadFetchResult.Found([Observation(goodDay)]));
        source.FetchDayAsync(PriceArea.NO2, badDay, Arg.Any<CancellationToken>())
            .Returns(DayAheadFetchResult.Failed("upstream 500"));

        var repository = Substitute.For<IPriceObservationRepository>();
        var runWriter = Substitute.For<IIngestionRunWriter>();
        var useCase = new IngestDayAheadPricesUseCase(source, repository, runWriter, TimeProvider.System);

        var result = await useCase.RunAsync(PriceArea.NO2, goodDay, badDay, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.Partial, result.Status);
        Assert.Equal(1, result.Rows);
        Assert.Contains("upstream 500", result.Error, StringComparison.Ordinal);
        await repository.Received(1).UpsertRangeAsync(
            Arg.Is<IReadOnlyList<PriceObservation>>(list => list.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task All_days_failed_records_failed_and_never_calls_upsert()
    {
        var day = new DateOnly(2026, 1, 1);
        var source = Substitute.For<IDayAheadPriceSource>();
        source.FetchDayAsync(PriceArea.NO2, day, Arg.Any<CancellationToken>())
            .Returns(DayAheadFetchResult.Failed("network error"));

        var repository = Substitute.For<IPriceObservationRepository>();
        var runWriter = Substitute.For<IIngestionRunWriter>();
        var useCase = new IngestDayAheadPricesUseCase(source, repository, runWriter, TimeProvider.System);

        var result = await useCase.RunAsync(PriceArea.NO2, day, day, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.Failed, result.Status);
        Assert.Equal(0, result.Rows);
        await repository.DidNotReceive().UpsertRangeAsync(
            Arg.Any<IReadOnlyList<PriceObservation>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Not_yet_published_day_is_not_an_error()
    {
        // docs/DATA.md §1: "Absence before 14:30 is normal, not an error."
        var day = new DateOnly(2026, 1, 1);
        var source = Substitute.For<IDayAheadPriceSource>();
        source.FetchDayAsync(PriceArea.NO2, day, Arg.Any<CancellationToken>())
            .Returns(DayAheadFetchResult.NotYetPublished());

        var repository = Substitute.For<IPriceObservationRepository>();
        var runWriter = Substitute.For<IIngestionRunWriter>();
        var useCase = new IngestDayAheadPricesUseCase(source, repository, runWriter, TimeProvider.System);

        var result = await useCase.RunAsync(PriceArea.NO2, day, day, CancellationToken.None);

        Assert.Equal(IngestionRunStatus.Ok, result.Status);
        Assert.Equal(0, result.Rows);
        Assert.Null(result.Error);
    }
}

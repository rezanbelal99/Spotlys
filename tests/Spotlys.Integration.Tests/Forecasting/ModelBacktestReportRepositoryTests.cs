using Microsoft.EntityFrameworkCore;
using Spotlys.Infrastructure.Forecasting;

namespace Spotlys.Integration.Tests.Forecasting;

/// <summary>
/// Real Postgres, a real jsonb column -- exercises the report_json parsing
/// <see cref="ModelBacktestReportRepository"/> does against the exact shape
/// python/spotlys_model/training/report.py's <c>build_report_json</c> produces, not a
/// mocked JSON string. This is the correctness-critical path of the two files (a field
/// rename on either side breaks it silently otherwise -- ADR 0002's "Python writes, .NET
/// reads" split has no compiler to catch that).
/// </summary>
public sealed class ModelBacktestReportRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string ReportJson = """
        {
          "baselines": [{"lead_bucket": "0-24h", "baseline": "b1", "n": 100, "mae_ore_per_kwh": 12.5, "skill_vs_b1": 0.0}],
          "quantile_model": {
            "A": [
              {"lead_bucket": "0-24h", "n": 100, "mae_ore_per_kwh": 9.1, "skill_vs_b1": 0.272, "pinball_loss": 4.4, "coverage_50": 0.48, "coverage_90": 0.87},
              {"lead_bucket": "24-48h", "n": 100, "mae_ore_per_kwh": 11.3, "skill_vs_b1": 0.05, "pinball_loss": 5.2, "coverage_50": 0.51, "coverage_90": 0.90}
            ],
            "B": [
              {"lead_bucket": "0-24h", "n": 80, "mae_ore_per_kwh": 7.0, "skill_vs_b1": 0.4, "pinball_loss": 3.1, "coverage_50": 0.49, "coverage_90": 0.88}
            ]
          },
          "decision_regret": {
            "n_days": 42,
            "mean_regret_vs_perfect_foresight_nok": 3.2,
            "mean_regret_vs_charge_on_arrival_nok": -6.5,
            "mean_regret_vs_always_0200_nok": -1.1,
            "median_regret_vs_charge_on_arrival_nok": -5.9,
            "median_regret_vs_always_0200_nok": -0.8
          }
        }
        """;

    [Fact]
    public async Task GetLatestAsync_parses_regime_a_lead_buckets_and_regret_from_the_real_python_report_shape()
    {
        await using var db = fixture.CreateDbContext();
        var generatedAt = new DateTimeOffset(2026, 3, 1, 3, 0, 0, TimeSpan.Zero);
        db.ModelBacktestReports.Add(new ModelBacktestReportRow { Zone = "NO2", GeneratedAtUtc = generatedAt, ReportJson = ReportJson });
        await db.SaveChangesAsync();

        var repository = new ModelBacktestReportRepository(db);
        var report = await repository.GetLatestAsync("NO2", CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal("NO2", report!.Zone);
        Assert.Equal(generatedAt, report.GeneratedAtUtc);

        // Regime A only -- Regime B's own rows exist in the raw JSON but aren't surfaced by
        // this minimal report (see IModelSkillRepository's own doc comment).
        Assert.Equal(2, report.ByLeadBucket.Count);
        var first = report.ByLeadBucket[0];
        Assert.Equal("0-24h", first.LeadBucket);
        Assert.Equal(9.1m, first.MaeOrePerKwh);
        Assert.Equal(0.272m, first.SkillVsB1);
        Assert.Equal(0.87m, first.Coverage90);

        Assert.Equal(-6.5m, report.Regret.RegretVsChargeOnArrivalNok);
        Assert.Equal(-1.1m, report.Regret.RegretVsAlways0200Nok);
        Assert.Equal(42, report.Regret.BacktestDays);
    }

    [Fact]
    public async Task GetLatestAsync_returns_the_most_recently_generated_report_when_several_exist()
    {
        await using var db = fixture.CreateDbContext();
        const string zone = "NO3"; // a fixed, distinct zone -- isolates this test's rows
                                   // from the other tests' NO2/NO1 zones in the shared
                                   // Postgres fixture without relying on execution order
        db.ModelBacktestReports.AddRange(
            new ModelBacktestReportRow { Zone = zone, GeneratedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), ReportJson = ReportJson },
            new ModelBacktestReportRow { Zone = zone, GeneratedAtUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), ReportJson = ReportJson });
        await db.SaveChangesAsync();

        var repository = new ModelBacktestReportRepository(db);
        var report = await repository.GetLatestAsync(zone, CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), report!.GeneratedAtUtc);
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_when_no_report_has_been_published_for_the_zone()
    {
        await using var db = fixture.CreateDbContext();
        var repository = new ModelBacktestReportRepository(db);

        var report = await repository.GetLatestAsync("NO1", CancellationToken.None);

        Assert.Null(report);
    }
}

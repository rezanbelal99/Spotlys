using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Spotlys.Application.Forecasting;

namespace Spotlys.Infrastructure.Forecasting;

// Parses the JSON shape python/spotlys_model/training/report.py's build_report_json
// produces. No shared C# type for the document (ADR 0002) -- just the fields this
// repository actually surfaces through IModelSkillRepository. A malformed report_json
// (a real bug, not an operational state -- unlike "no report published yet", which
// GetLatestAsync's null return already covers) is left to throw and become a generic
// Problem Details 500 rather than being silently swallowed into an empty result.
internal sealed class ModelBacktestReportRepository(SpotlysDbContext dbContext) : IModelSkillRepository
{
    public async Task<ModelSkillReport?> GetLatestAsync(string zone, CancellationToken ct)
    {
        var row = await dbContext.ModelBacktestReports
            .Where(r => r.Zone == zone)
            .OrderByDescending(r => r.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return row is null ? null : Parse(row);
    }

    private static ModelSkillReport Parse(ModelBacktestReportRow row)
    {
        using var document = JsonDocument.Parse(row.ReportJson);
        var root = document.RootElement;

        var byLeadBucket = new List<LeadBucketSkill>();
        if (root.TryGetProperty("quantile_model", out var quantileModel) &&
            quantileModel.TryGetProperty("A", out var regimeA))
        {
            foreach (var bucket in regimeA.EnumerateArray())
            {
                byLeadBucket.Add(new LeadBucketSkill(
                    bucket.GetProperty("lead_bucket").GetString()!,
                    bucket.GetProperty("mae_ore_per_kwh").GetDecimal(),
                    bucket.GetProperty("skill_vs_b1").GetDecimal(),
                    bucket.GetProperty("pinball_loss").GetDecimal(),
                    bucket.GetProperty("coverage_50").GetDecimal(),
                    bucket.GetProperty("coverage_90").GetDecimal()));
            }
        }

        var regretElement = root.GetProperty("decision_regret");
        var regret = new DecisionRegret(
            regretElement.GetProperty("mean_regret_vs_charge_on_arrival_nok").GetDecimal(),
            regretElement.GetProperty("mean_regret_vs_always_0200_nok").GetDecimal(),
            regretElement.GetProperty("n_days").GetInt32());

        return new ModelSkillReport(row.Zone, byLeadBucket, regret, row.GeneratedAtUtc);
    }
}

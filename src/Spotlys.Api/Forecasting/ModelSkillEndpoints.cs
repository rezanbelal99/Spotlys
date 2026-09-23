using Spotlys.Application.Forecasting;

namespace Spotlys.Api.Forecasting;

internal sealed record LeadBucketSkillDto(
    string LeadBucket, decimal MaeOrePerKwh, decimal SkillVsB1, decimal PinballLoss, decimal Coverage50, decimal Coverage90)
{
    public static LeadBucketSkillDto FromDomain(LeadBucketSkill skill) => new(
        skill.LeadBucket, skill.MaeOrePerKwh, skill.SkillVsB1, skill.PinballLoss, skill.Coverage50, skill.Coverage90);
}

internal sealed record DecisionRegretDto(decimal RegretVsChargeOnArrivalNok, decimal RegretVsAlways0200Nok, int BacktestDays)
{
    public static DecisionRegretDto FromDomain(DecisionRegret regret) => new(
        regret.RegretVsChargeOnArrivalNok, regret.RegretVsAlways0200Nok, regret.BacktestDays);
}

internal sealed record ModelSkillResponseDto(
    string Zone,
    IReadOnlyList<LeadBucketSkillDto> ByLeadBucket,
    DecisionRegretDto Regret,
    DateTimeOffset ReportGeneratedAtUtc)
{
    public static ModelSkillResponseDto FromDomain(ModelSkillReport report) => new(
        report.Zone,
        report.ByLeadBucket.Select(LeadBucketSkillDto.FromDomain).ToList(),
        DecisionRegretDto.FromDomain(report.Regret),
        report.GeneratedAtUtc);
}

internal static class ModelSkillEndpoints
{
    // Public, no auth (docs/ARCHITECTURE.md §6: "the dashboard has a public /model page" --
    // recruiters won't create an account, and this is aggregate model performance, not
    // anyone's personal data). Output-cached like the other public read endpoints; a new
    // backtest report only lands via model.yml, so a short cache window costs nothing real.
    public static RouteGroupBuilder MapModelSkillEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/model/skill/{zone}", GetSkillAsync)
            .WithName("ModelSkill")
            .CacheOutput(policy => policy
                .Expire(TimeSpan.FromMinutes(15))
                .SetVaryByRouteValue("zone"));

        return group;
    }

    private static async Task<IResult> GetSkillAsync(
        string zone, IModelSkillRepository skillRepository, CancellationToken ct)
    {
        var report = await skillRepository.GetLatestAsync(zone, ct).ConfigureAwait(false);
        if (report is null)
        {
            return Results.Problem(
                title: "No backtest report published yet",
                detail: $"No model.yml run has published a backtest report for zone '{zone}' yet.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(ModelSkillResponseDto.FromDomain(report));
    }
}

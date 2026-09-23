namespace Spotlys.Application.Common;

/// <summary>docs/FORECASTING.md §8 / CLAUDE.md rule 8: a range plus an expected value,
/// never a bare number -- shared by every use case that turns a forecast fan into a cost
/// figure (<c>Scheduling.PlanChargeUseCase</c>, <c>Pricing.RegimeAdvisorUseCase</c>).
/// </summary>
public sealed record CostRange(decimal LowExVatNok, decimal HighExVatNok, decimal ExpectedExVatNok);

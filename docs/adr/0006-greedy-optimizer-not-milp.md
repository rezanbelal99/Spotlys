# 0006 — A greedy peak-ceiling optimizer, not a general MILP

- **Status:** Accepted
- **Date:** 2026-09-17
- **Supersedes:** —

## Context

Scheduling a flexible load (docs/FORECASTING.md §8) against an hourly cost forecast, subject
to a power ceiling, a deadline, and a capacity-step tariff that couples the whole month
through the top-3 peak, is a scheduling/allocation problem. The general version of "minimise
cost subject to linear constraints and a small number of discrete step choices" is a mixed
integer linear program, and Google OR-Tools or a similar solver could express it directly.

For a single load and a fixed household, though, the problem decomposes cleanly:

- The capacity term only changes value at the grid company's own step boundaries (5-8 of
  them), so the outer choice is "which of a handful of ceilings" rather than a continuous
  variable.
- For a *fixed* ceiling, the remaining problem — allocate `EnergyKwh` across hours, bounded
  by `MaxPowerKw` and headroom under the ceiling, minimising cost — is separable per hour and
  solved exactly by sorting hours by marginal cost and filling greedily. There's no coupling
  between hours once the ceiling is fixed.

## Decision

Two nested loops, no general-purpose solver: an inner `O(n log n)` greedy fill per candidate
ceiling, an outer loop over the grid company's own step boundaries. Implemented in
`Spotlys.Domain.Scheduling.LoadOptimizer`, pure and dependency-free.

## Consequences

**Good**

- Runs in microseconds — fast enough to call synchronously from `POST /api/v1/plan`, no
  background job or solver license.
- Provably optimal for this problem's actual structure (a fixed ceiling's inner allocation
  is a textbook exchange-argument greedy; the outer loop is an exhaustive search over the
  only ceilings where cost changes).
- Explainable to a user in one sentence — "cheapest hours first, up to your capacity step" —
  which a MILP's solution is not. docs/DESIGN.md §4's "begrenset av ..." sentence depends on
  this.
- No new dependency (a MILP solver, even a good one, is a real addition to justify per
  CLAUDE.md's "don't add dependencies casually").

**Bad / costs**

- Doesn't generalise: this decomposition relies on there being one flexible load and one
  household. Multiple coupled loads, a battery, or solar export reintroduce coupling between
  hours that the greedy fill can't handle — those are genuinely different problems.
- The `Lowering_one_hours_price_never_decreases_that_hours_allocation` property is verified
  empirically over generated cases (CsCheck), not proven for the ceiling-selection
  interaction in full generality — stated honestly in
  `tests/Spotlys.Domain.Tests/Scheduling/LoadOptimizerPropertyTests.cs` rather than
  overclaimed.

**Revisit if**

- Phase 6's optional battery/solar scheduling depth is picked up (docs/ROADMAP.md Phase 6):
  swap the inner solver for an LP via Google OR-Tools behind the same
  `LoadOptimizer.Optimize`-shaped interface, per FORECASTING.md §8's own note. This ADR
  should be superseded, not quietly ignored, if that happens.

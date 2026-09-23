"""
Decision regret (docs/FORECASTING.md §6): "for each backtest day, run the optimizer twice
-- once on the forecast, once on the realised prices (perfect foresight) -- and record the
difference in the actual bill". This is the only place in the project that answers "does
using this thing cost me less money than not using it", so it plugs directly into the
model.yml report rather than living only as an internal metric.

Deliberately duplicates only the greedy-allocation-by-marginal-cost core of
`Spotlys.Domain.Scheduling.LoadOptimizer` (docs/adr/0002: Python trains, .NET serves -- no
cross-process hop for a per-request optimizer call, so this Python port exists purely to
score historical folds, never to schedule anything for real). Two things are cut relative
to the C# optimizer, both named in the Phase 4 plan: no capacity-step outer loop (one fixed
`MAX_POWER_KW` ceiling, not a real per-meter tariff's step table), and one standard EV task
rather than the general per-user problem. If this file ever needs to grow beyond the
regret metric, that's a sign it should become a shared spec test against the C# optimizer
instead of a second implementation.

STROMSTOTTE_THRESHOLD_ORE/SUPPORT_RATE below are illustrative constants for this aggregate,
multi-year historical metric -- not the live governed values `scheme_parameter` holds.
CLAUDE.md rule 3 ("a bare 77 or 50 in pricing code fails review") targets the product's own
pricing code, which computes what a real user actually pays right now; this file is a
backtest analysis script scoring folds spanning years during which the real threshold
changed by political decision, so reading a single "current" value from the versioned
table would be exactly as arbitrary as a named constant while adding a live DB dependency
to what is otherwise a pure, testable function of two pandas Series. Named, commented, and
not silently hidden -- the same treatment ADR 0002 already gives this file's other
duplication.
"""

from __future__ import annotations

import pandas as pd

STROMSTOTTE_THRESHOLD_ORE = 77.0
STROMSTOTTE_SUPPORT_RATE = 0.9

# One standard EV task -- a typical home charger topping up overnight, not the general
# per-user problem (docs/FORECASTING.md §8's own FlexibleLoad has more knobs; this fixes
# all of them to one illustrative case for a single, comparable regret figure).
EV_ENERGY_KWH = 40.0
EV_MAX_POWER_KW = 7.0
EV_WINDOW_HOURS = 24  # "ready by tomorrow morning"

OSLO_TZ = "Europe/Oslo"


def marginal_cost_ore(spot_ore_per_kwh: pd.Series) -> pd.Series:
    """docs/DOMAIN.md §3a's strømstøtte formula, ex markup (none in this illustrative
    task): the state covers 90% of the spread above the threshold, so the marginal cost of
    an expensive hour flattens hard once both it and the alternative are above threshold --
    the whole point of pricing schedules on this rather than raw spot spread."""
    support = STROMSTOTTE_SUPPORT_RATE * (spot_ore_per_kwh - STROMSTOTTE_THRESHOLD_ORE).clip(lower=0.0)
    return spot_ore_per_kwh - support


def greedy_cost_schedule(energy_kwh: float, max_power_kw: float, driving_prices: pd.Series) -> pd.Series:
    """Sorts candidate hours by marginal cost ascending and fills each up to
    `max_power_kw` until `energy_kwh` is satisfied -- the O(n log n) inner loop
    FORECASTING.md §8 describes, minus the capacity-step outer loop (see module docstring).
    Returns the chosen kWh per hour (a sparse Series indexed by the hours actually used).
    """
    cost = marginal_cost_ore(driving_prices).sort_values(kind="stable")
    remaining = energy_kwh
    allocated: dict[pd.Timestamp, float] = {}
    for hour in cost.index:
        if remaining <= 0:
            break
        take = min(remaining, max_power_kw)
        allocated[hour] = take
        remaining -= take
    return pd.Series(allocated, name="allocated_kwh")


def naive_immediate_schedule(energy_kwh: float, max_power_kw: float, hours_in_order: pd.DatetimeIndex) -> pd.Series:
    """"Charge on arrival" -- fills the earliest hours in the window regardless of price,
    the same naive counterfactual `LoadOptimizer.NaiveImmediateSchedule` prices in the
    live `/plan` endpoint."""
    remaining = energy_kwh
    allocated: dict[pd.Timestamp, float] = {}
    for hour in hours_in_order:
        if remaining <= 0:
            break
        take = min(remaining, max_power_kw)
        allocated[hour] = take
        remaining -= take
    return pd.Series(allocated, name="allocated_kwh")


def naive_always_0200_schedule(energy_kwh: float, max_power_kw: float, hours_in_order: pd.DatetimeIndex) -> pd.Series:
    """"Always plug in at 02:00" -- a common piece of received wisdom in Norway (night
    rates start at 22:00, so 02:00 is deep inside the cheap window on energiledd, but says
    nothing about the *spot* price, which this naive strategy ignores entirely). Starts at
    the first Oslo-local 02:00 in the window and fills forward from there; if none exists
    (a window entirely before the first 02:00), falls back to the earliest hour, matching
    "plug in now" rather than inventing an hour outside the actual window."""
    local_hours = hours_in_order.tz_convert(OSLO_TZ)
    start_positions = [i for i, h in enumerate(local_hours) if h.hour == 2]
    start = start_positions[0] if start_positions else 0
    ordered = list(hours_in_order[start:]) + list(hours_in_order[:start])
    return naive_immediate_schedule(energy_kwh, max_power_kw, pd.DatetimeIndex(ordered))


def schedule_cost_nok(schedule: pd.Series, realised_prices: pd.Series) -> float:
    """Re-prices a chosen hour-by-hour allocation at `realised_prices` -- this is what
    turns "which hours were chosen" into "what did that choice actually cost", the step
    regret is built from (a schedule chosen on a forecast, priced on what really
    happened)."""
    cost_ore_per_kwh = marginal_cost_ore(realised_prices.reindex(schedule.index))
    return float((schedule * cost_ore_per_kwh).sum() / 100.0)


def run_regret_backtest(price: pd.Series, forecast_results: pd.DataFrame) -> pd.DataFrame:
    """One row per `as_of` in `forecast_results` (the walk-forward backtest's own output --
    see `training.lightgbm_model.run_lightgbm_backtest` -- must carry `as_of`,
    `target_time`, `q50`, and `actual` columns). For each `as_of`, takes the first
    `EV_WINDOW_HOURS` target hours as the EV task's window, schedules it on the model's q50
    forecast and on perfect foresight (the realised prices themselves), then re-prices both
    -- plus the two naive strategies, which need no forecast at all -- against what
    actually happened. Rows where the window isn't fully known (a short fold edge) are
    skipped, not padded (docs/DATA.md §6: never silently fill a gap)."""
    rows = []
    for as_of, group in forecast_results.groupby("as_of"):
        window = group.sort_values("target_time").head(EV_WINDOW_HOURS)
        if len(window) < EV_WINDOW_HOURS or window["actual"].isna().any() or window["q50"].isna().any():
            continue

        target_times = pd.DatetimeIndex(window["target_time"])
        actual = pd.Series(window["actual"].to_numpy(), index=target_times)
        forecast = pd.Series(window["q50"].to_numpy(), index=target_times)

        forecast_schedule = greedy_cost_schedule(EV_ENERGY_KWH, EV_MAX_POWER_KW, forecast)
        perfect_schedule = greedy_cost_schedule(EV_ENERGY_KWH, EV_MAX_POWER_KW, actual)
        arrival_schedule = naive_immediate_schedule(EV_ENERGY_KWH, EV_MAX_POWER_KW, target_times)
        always_0200_schedule = naive_always_0200_schedule(EV_ENERGY_KWH, EV_MAX_POWER_KW, target_times)

        forecast_cost_nok = schedule_cost_nok(forecast_schedule, actual)
        perfect_cost_nok = schedule_cost_nok(perfect_schedule, actual)
        arrival_cost_nok = schedule_cost_nok(arrival_schedule, actual)
        always_0200_cost_nok = schedule_cost_nok(always_0200_schedule, actual)

        rows.append(
            {
                "as_of": as_of,
                "forecast_cost_nok": forecast_cost_nok,
                "perfect_foresight_cost_nok": perfect_cost_nok,
                "charge_on_arrival_cost_nok": arrival_cost_nok,
                "always_0200_cost_nok": always_0200_cost_nok,
                # Positive = the forecast-driven schedule cost more than the comparison --
                # i.e. real money left on the table. Negative means the forecast beat it.
                "regret_vs_perfect_foresight_nok": forecast_cost_nok - perfect_cost_nok,
                "regret_vs_charge_on_arrival_nok": forecast_cost_nok - arrival_cost_nok,
                "regret_vs_always_0200_nok": forecast_cost_nok - always_0200_cost_nok,
            }
        )

    return pd.DataFrame(rows)


def summarize_regret(results: pd.DataFrame) -> dict[str, float]:
    """Mean and median regret across every scored day, reported plainly (docs/FORECASTING.md
    §6/§7: given the model's already-known mixed skill, regret against a naive strategy can
    itself be unimpressive or even negative in some periods -- that's reported as-is, never
    smoothed into a flattering single figure)."""
    if results.empty:
        return {
            "n_days": 0,
            "mean_regret_vs_perfect_foresight_nok": float("nan"),
            "mean_regret_vs_charge_on_arrival_nok": float("nan"),
            "mean_regret_vs_always_0200_nok": float("nan"),
            "median_regret_vs_charge_on_arrival_nok": float("nan"),
            "median_regret_vs_always_0200_nok": float("nan"),
        }

    return {
        "n_days": len(results),
        "mean_regret_vs_perfect_foresight_nok": float(results["regret_vs_perfect_foresight_nok"].mean()),
        "mean_regret_vs_charge_on_arrival_nok": float(results["regret_vs_charge_on_arrival_nok"].mean()),
        "mean_regret_vs_always_0200_nok": float(results["regret_vs_always_0200_nok"].mean()),
        "median_regret_vs_charge_on_arrival_nok": float(results["regret_vs_charge_on_arrival_nok"].median()),
        "median_regret_vs_always_0200_nok": float(results["regret_vs_always_0200_nok"].median()),
    }

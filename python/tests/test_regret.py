"""Targeted example tests against hand-built fixtures -- this file confirms the regret
port produces sane output, not exhaustive invariant coverage. The exhaustive invariant
coverage for the greedy-allocation core (total energy scheduled, never exceeding max
power, cost never worse than a naive time-ordered fill) already lives in
tests/Spotlys.Domain.Tests/Scheduling/LoadOptimizerPropertyTests.cs against the real C#
optimizer this file duplicates a narrow slice of (see regret.py's module docstring)."""

from __future__ import annotations

import pandas as pd
import pytest

from spotlys_model.training.regret import (
    EV_ENERGY_KWH,
    EV_MAX_POWER_KW,
    greedy_cost_schedule,
    marginal_cost_ore,
    naive_always_0200_schedule,
    naive_immediate_schedule,
    run_regret_backtest,
    schedule_cost_nok,
    summarize_regret,
)


def _hours(start: str, n: int) -> pd.DatetimeIndex:
    return pd.date_range(start, periods=n, freq="h", tz="UTC")


class TestMarginalCostOre:
    def test_below_threshold_is_unchanged(self):
        spot = pd.Series([20.0, 50.0, 76.9])
        result = marginal_cost_ore(spot)
        pd.testing.assert_series_equal(result, spot)

    def test_above_threshold_only_pays_ten_percent_of_the_spread(self):
        # 177 ore is 100 ore above the 77-ore threshold -- 90% support means only 10 ore of
        # that spread reaches the marginal cost, on top of the threshold itself.
        spot = pd.Series([177.0])
        result = marginal_cost_ore(spot)
        assert result.iloc[0] == 87.0


class TestGreedyCostSchedule:
    def test_fills_the_cheapest_hours_first(self):
        hours = _hours("2026-01-15T00:00", 4)
        prices = pd.Series([200.0, 20.0, 300.0, 90.0], index=hours)

        schedule = greedy_cost_schedule(energy_kwh=10.0, max_power_kw=7.0, driving_prices=prices)

        # Cheapest hour (index 1, 20 ore) fills first at the 7kW ceiling, then the
        # next-cheapest (index 3, 90 ore) tops up the remaining 3 kWh.
        assert schedule[hours[1]] == 7.0
        assert schedule[hours[3]] == 3.0
        assert hours[0] not in schedule.index
        assert hours[2] not in schedule.index
        assert schedule.sum() == 10.0

    def test_never_exceeds_max_power_in_any_hour(self):
        hours = _hours("2026-01-15T00:00", 3)
        prices = pd.Series([10.0, 10.0, 10.0], index=hours)

        schedule = greedy_cost_schedule(energy_kwh=EV_ENERGY_KWH, max_power_kw=EV_MAX_POWER_KW, driving_prices=prices)

        assert (schedule <= EV_MAX_POWER_KW).all()
        # Only 3 hours available at 7kW each = 21kWh max reachable -- can't fully satisfy
        # a 40kWh request in this fixture, and the function must not invent extra hours.
        assert schedule.sum() == 21.0


class TestNaiveSchedules:
    def test_charge_on_arrival_fills_earliest_hours_regardless_of_price(self):
        hours = _hours("2026-01-15T00:00", 3)
        schedule = naive_immediate_schedule(energy_kwh=10.0, max_power_kw=7.0, hours_in_order=hours)

        assert schedule[hours[0]] == 7.0
        assert schedule[hours[1]] == 3.0
        assert hours[2] not in schedule.index

    def test_always_0200_starts_at_the_first_local_2am_in_the_window(self):
        # Window starts at 00:00 Oslo (winter, UTC+1) -- local 02:00 is the third hour in.
        hours = pd.date_range("2026-01-15T00:00", periods=5, freq="h", tz="Europe/Oslo").tz_convert("UTC")
        schedule = naive_always_0200_schedule(energy_kwh=7.0, max_power_kw=7.0, hours_in_order=hours)

        assert schedule[hours[2]] == 7.0
        assert len(schedule) == 1

    def test_always_0200_falls_back_to_the_earliest_hour_when_no_2am_exists_in_the_window(self):
        hours = _hours("2026-01-15T03:00", 2)  # a 2-hour window that never touches 02:00 local
        schedule = naive_always_0200_schedule(energy_kwh=5.0, max_power_kw=5.0, hours_in_order=hours)

        assert schedule[hours[0]] == 5.0


class TestScheduleCostNok:
    def test_prices_the_schedule_at_the_given_series_not_the_one_it_was_chosen_on(self):
        hours = _hours("2026-01-15T00:00", 2)
        schedule = pd.Series([5.0, 5.0], index=hours)
        realised = pd.Series([20.0, 200.0], index=hours)  # far above threshold at index 1

        cost = schedule_cost_nok(schedule, realised)

        # hour0: 5 kWh * 20 ore (below threshold, unchanged) = 100 ore
        # hour1: 5 kWh * marginal_cost(200) = 5 * (77 + 0.10*123) = 5 * 89.3 = 446.5 ore
        expected_ore = 5.0 * 20.0 + 5.0 * 89.3
        assert cost == pytest.approx(expected_ore / 100.0, abs=1e-6)


class TestRunRegretBacktest:
    def test_perfect_foresight_never_costs_more_than_the_forecast_driven_schedule(self):
        # A deliberately wrong forecast (flat 100 ore every hour) against realised prices
        # with one very cheap hour the forecast can't see -- perfect foresight must find
        # that cheap hour and never cost more than the naive-forecast schedule.
        as_of = pd.Timestamp("2026-01-15T06:00:00Z")
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=24, freq="h")
        actual = [100.0] * 24
        actual[10] = 5.0  # one very cheap hour, invisible to a flat forecast
        forecast_results = pd.DataFrame(
            {
                "as_of": as_of,
                "target_time": target_times,
                "q50": [100.0] * 24,
                "actual": actual,
            }
        )

        results = run_regret_backtest(pd.Series(dtype=float), forecast_results)

        assert len(results) == 1
        row = results.iloc[0]
        assert row["regret_vs_perfect_foresight_nok"] >= 0.0
        assert row["perfect_foresight_cost_nok"] < row["forecast_cost_nok"]

    def test_skips_an_as_of_with_an_incomplete_window(self):
        # docs/DATA.md §6: never silently pad a gap -- a short window (fewer than
        # EV_WINDOW_HOURS rows) must be dropped, not truncated and scored anyway.
        as_of = pd.Timestamp("2026-01-15T06:00:00Z")
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=5, freq="h")
        forecast_results = pd.DataFrame(
            {"as_of": as_of, "target_time": target_times, "q50": [50.0] * 5, "actual": [50.0] * 5}
        )

        results = run_regret_backtest(pd.Series(dtype=float), forecast_results)

        assert results.empty

    def test_summarize_regret_on_empty_results_reports_nan_not_a_crash(self):
        summary = summarize_regret(pd.DataFrame())

        assert summary["n_days"] == 0
        assert pd.isna(summary["mean_regret_vs_perfect_foresight_nok"])

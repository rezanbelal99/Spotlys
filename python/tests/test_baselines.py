from __future__ import annotations

import numpy as np
import pandas as pd
import pytest

from spotlys_model.training.baselines import (
    all_baselines,
    b0_last_value,
    b1_seasonal_naive,
    b2_climatology,
    b3_damped_persistence,
    b4_shape_times_level,
    fit_b3_alpha,
)


def _series(start: pd.Timestamp, hours: int, fn) -> pd.Series:
    index = pd.date_range(start, periods=hours, freq="h")
    return pd.Series([fn(t) for t in index], index=index)


class TestB0LastValue:
    def test_matches_the_value_24h_before_the_target_when_within_reach(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(hours=100), 200, lambda t: t.hour)
        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=1)])  # 1h lead, well within 24h reach

        result = b0_last_value(price, as_of, target)

        expected = price[target[0] - pd.Timedelta(hours=24)]
        assert result.iloc[0] == expected

    def test_is_nan_beyond_its_24h_natural_horizon(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(hours=100), 400, lambda t: t.hour)
        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=48)])  # target-24h is after as_of

        result = b0_last_value(price, as_of, target)

        assert np.isnan(result.iloc[0])


class TestB1SeasonalNaive:
    def test_matches_the_same_hour_one_week_earlier(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(hours=300), 500, lambda t: t.hour + t.dayofweek * 24)
        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=100)])  # within 168h reach

        result = b1_seasonal_naive(price, as_of, target)

        expected = price[target[0] - pd.Timedelta(hours=168)]
        assert result.iloc[0] == expected

    def test_valid_across_the_full_168h_horizon(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(hours=300), 500, lambda t: 1.0)
        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=168)])  # the harness's max lead

        result = b1_seasonal_naive(price, as_of, target)

        assert not np.isnan(result.iloc[0])


class TestB2Climatology:
    def test_matches_a_manually_computed_median_for_the_same_hour_of_week(self):
        as_of = pd.Timestamp("2026-06-15T12:00:00Z")  # a Monday
        # Four Mondays at 06:00 in the trailing 28 days, distinct values.
        base = as_of - pd.Timedelta(days=28)
        index = pd.date_range(base, as_of, freq="h")
        price = pd.Series(np.random.default_rng(0).uniform(0, 100, len(index)), index=index)

        target = pd.DatetimeIndex([pd.Timestamp("2026-06-16T06:00:00Z")])  # a Tuesday 06:00

        result = b2_climatology(price, as_of, target)

        visible = price[(price.index <= as_of) & (price.index > as_of - pd.Timedelta(days=28))]
        expected = visible[(visible.index.dayofweek == 1) & (visible.index.hour == 6)].median()
        assert result.iloc[0] == expected

    def test_never_reads_a_row_after_as_of(self):
        as_of = pd.Timestamp("2026-06-15T12:00:00Z")
        index = pd.date_range(as_of - pd.Timedelta(days=28), as_of + pd.Timedelta(days=5), freq="h")
        price = pd.Series(1.0, index=index)
        price.loc[price.index > as_of] = 999_999.0  # sentinel for "must never be read"

        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=100)])
        result = b2_climatology(price, as_of, target)

        assert result.iloc[0] == 1.0


class TestB3DampedPersistence:
    def test_alpha_one_reduces_to_the_last_known_value(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(hours=200), 201, lambda t: t.hour)
        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=50)])

        result = b3_damped_persistence(price, as_of, target, alpha=1.0)

        assert result.iloc[0] == price[as_of]

    def test_alpha_zero_reduces_to_the_trailing_168h_mean(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(hours=200), 201, lambda t: t.hour)
        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=50)])

        result = b3_damped_persistence(price, as_of, target, alpha=0.0)

        window = price[(price.index > as_of - pd.Timedelta(hours=168)) & (price.index <= as_of)]
        assert result.iloc[0] == window.mean()

    def test_fit_b3_alpha_prefers_pure_persistence_when_todays_value_exactly_predicts_tomorrows(self):
        # A pure hour-of-day pattern, identical on every calendar day -- p(as_of) exactly
        # equals p(as_of + 24h) for any as_of, so alpha=1 (pure last-value) has zero error
        # and must win outright over any blend with the rolling mean.
        base_time = pd.Timestamp("2026-01-01T00:00:00Z")
        n = 24 * 60
        index = pd.date_range(base_time, periods=n, freq="h")
        price = pd.Series([10.0 + t.hour for t in index], index=index)  # same shape every day

        as_of_values = [index[i] for i in range(200, n - 200, 24)]
        alpha = fit_b3_alpha(price, as_of_values, target_hours_ahead=24)

        assert alpha == 1.0


class TestB4ShapeTimesLevel:
    def test_a_flat_series_produces_a_flat_prediction_equal_to_its_own_level(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(days=10), 24 * 10 + 1, lambda t: 42.0)
        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=h) for h in (1, 50, 150)])

        result = b4_shape_times_level(price, as_of, target)

        assert np.allclose(result.to_numpy(), 42.0)

    def test_never_reads_a_row_after_as_of(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        index = pd.date_range(as_of - pd.Timedelta(days=10), as_of + pd.Timedelta(days=5), freq="h")
        price = pd.Series(10.0, index=index)
        price.loc[price.index > as_of] = 999_999.0

        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=100)])
        result = b4_shape_times_level(price, as_of, target)

        assert result.iloc[0] < 1000  # nowhere near the sentinel

    def test_yesterday_boundary_is_the_oslo_calendar_day_not_utc(self):
        # 01:00 Oslo, June 16 -- Oslo's and UTC's calendar dates disagree at this instant,
        # which is exactly the case the pre-fix (UTC-normalized) version got wrong.
        as_of = pd.Timestamp("2026-06-15T23:00:00Z")
        oslo_yesterday_start = pd.Timestamp("2026-06-15T00:00:00", tz="Europe/Oslo")
        oslo_yesterday_index = pd.date_range(
            oslo_yesterday_start, periods=24, freq="h", tz="Europe/Oslo"
        ).tz_convert("UTC")

        # Zero everywhere in the trailing 7-day level window except the true Oslo-yesterday
        # window. Under the old UTC-day bug, "yesterday" would land on an all-zero window
        # and the function would return NaN (the yesterday.mean() == 0 guard) instead of a
        # real prediction.
        full_index = pd.date_range(as_of - pd.Timedelta(days=8), as_of, freq="h")
        price = pd.Series(0.0, index=full_index)
        price.loc[price.index.isin(oslo_yesterday_index)] = 60.0

        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=1)])
        result = b4_shape_times_level(price, as_of, target)

        assert not np.isnan(result.iloc[0])
        expected_level = price[price.index > as_of - pd.Timedelta(days=7)].mean()
        assert result.iloc[0] == pytest.approx(expected_level)  # shape is flat 1.0 (yesterday is a flat 60.0)

    def test_shape_profile_uses_oslo_local_hour_of_day_not_utc_hour(self):
        as_of = pd.Timestamp("2026-06-15T23:00:00Z")  # 01:00 Oslo, June 16
        oslo_yesterday_start = pd.Timestamp("2026-06-15T00:00:00", tz="Europe/Oslo")
        oslo_yesterday_index = pd.date_range(oslo_yesterday_start, periods=24, freq="h", tz="Europe/Oslo")

        # Yesterday's price is flat except a spike at Oslo-local hour 14 -- a UTC-hour
        # grouping would put the spike at a different (UTC) hour, shifted by Oslo's summer
        # offset from UTC, so it would show up at the wrong target hour below.
        values = [1.0] * 24
        values[14] = 100.0
        price = pd.Series(values, index=oslo_yesterday_index.tz_convert("UTC"))
        padding_index = pd.date_range(as_of - pd.Timedelta(days=7), as_of, freq="h").difference(price.index)
        price = pd.concat([price, pd.Series(0.0, index=padding_index)]).sort_index()

        target_oslo_14 = pd.Timestamp("2026-06-17T14:00:00", tz="Europe/Oslo").tz_convert("UTC")
        target_oslo_10 = pd.Timestamp("2026-06-17T10:00:00", tz="Europe/Oslo").tz_convert("UTC")
        result = b4_shape_times_level(price, as_of, pd.DatetimeIndex([target_oslo_14, target_oslo_10]))

        # Oslo hour 14's prediction must be dramatically higher than Oslo hour 10's -- if
        # hour grouping used UTC instead, the spike would land on the wrong pair of hours.
        assert result.iloc[0] > result.iloc[1] * 10

    def test_yesterday_boundary_is_correct_across_the_fall_back_transition(self):
        # docs/ARCHITECTURE.md §3: a DST fixture is mandatory. Regression test matching
        # features/builder.py's own DST test for the same class of bug.
        as_of = pd.Timestamp("2026-10-26T11:00:00Z")  # 12:00 Oslo (CET), the day after fall-back
        yesterday_oslo_start = pd.Timestamp("2026-10-25T00:00:00", tz="Europe/Oslo")  # the transition day
        yesterday_index = pd.date_range(yesterday_oslo_start, periods=25, freq="h", tz="Europe/Oslo").tz_convert("UTC")

        padding_index = pd.date_range(as_of - pd.Timedelta(days=7), as_of, freq="h").difference(yesterday_index)
        price = pd.concat(
            [pd.Series(70.0, index=yesterday_index), pd.Series(0.0, index=padding_index)]
        ).sort_index()

        target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=1)])
        result = b4_shape_times_level(price, as_of, target)

        assert not np.isnan(result.iloc[0])


class TestAllBaselines:
    def test_returns_one_row_per_target_time_with_all_five_columns(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(days=40), 24 * 40 + 1, lambda t: t.hour + 1.0)
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=24, freq="h")

        frame = all_baselines(price, as_of, target_times, b3_alpha=0.5)

        assert len(frame) == 24
        assert list(frame.columns) == ["as_of", "target_time", "lead_hours", "b0", "b1", "b2", "b3", "b4"]

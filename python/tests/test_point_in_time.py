"""
The non-negotiable suite (docs/FORECASTING.md §5): for random historic as_of values,
assert no feature reads data observed later. Written before baselines.py or the backtest
harness, per the explicit instruction this phase was built under.
"""

from __future__ import annotations

import random

import numpy as np
import pandas as pd
import pytest

from spotlys_model.features.builder import (
    PointInTimeViolation,
    _assert_available,
    build_features,
)
from spotlys_model.features.manifest import FeatureSpec


def _synthetic_price_series(start: pd.Timestamp, hours: int) -> pd.Series:
    index = pd.date_range(start, periods=hours, freq="h")
    # Deterministic but non-trivial values -- if a wrong hour ever leaks in, its value is
    # distinguishable from its neighbours (unlike e.g. all-zeros).
    values = np.sin(np.arange(hours) / 7.0) * 100 + np.arange(hours) * 0.01
    return pd.Series(values, index=index)


class TestAssertAvailable:
    """The manifest-enforcement mechanism itself, isolated from any real feature's
    current (all-zero) lag values -- this is what will actually fire once weather/hydrology
    features with a genuine publication delay are wired in at step 5."""

    def test_data_available_before_as_of_passes(self):
        spec = FeatureSpec("test", "test_source", availability_lag_hours=6.0, regime="both")
        source_time = pd.Timestamp("2026-06-15T00:00:00Z")
        as_of = source_time + pd.Timedelta(hours=6)  # exactly at the availability boundary

        _assert_available(spec, source_time, as_of)  # must not raise

    def test_data_available_one_hour_too_late_raises(self):
        spec = FeatureSpec("test", "test_source", availability_lag_hours=6.0, regime="both")
        source_time = pd.Timestamp("2026-06-15T00:00:00Z")
        as_of = source_time + pd.Timedelta(hours=5)  # one hour short of the 6h lag

        with pytest.raises(PointInTimeViolation, match="test"):
            _assert_available(spec, source_time, as_of)


class TestBuildFeaturesNeverLeaksFutureData:
    def test_extra_future_rows_in_the_input_series_never_change_the_output(self):
        # The classic leakage bug: a caller passes the whole historical dataframe instead
        # of truncating it to as_of. build_features must produce byte-identical output
        # either way -- proving future data has zero influence, not just that nothing
        # obviously crashed. Uses a Regime A as_of (before the 13:00 Oslo publication
        # clock) deliberately: Regime B's D+1 curve feature is a narrow, *intentional*
        # exception to this property, covered separately in TestRegimeBD1CurveException.
        as_of = pd.Timestamp("2026-06-15T06:00:00Z")  # 08:00 Oslo (CEST) -- Regime A
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=5, freq="h")
        price = _synthetic_price_series(as_of - pd.Timedelta(hours=200), 200)

        features_clean = build_features(price, as_of, target_times)

        future_index = pd.date_range(as_of + pd.Timedelta(hours=1), periods=200, freq="h")
        future_price = pd.Series(np.full(len(future_index), 999_999.0), index=future_index)
        price_with_smuggled_future_rows = pd.concat([price, future_price])

        features_with_leakage_attempt = build_features(price_with_smuggled_future_rows, as_of, target_times)

        pd.testing.assert_frame_equal(features_clean, features_with_leakage_attempt)

    def test_a_row_exactly_at_as_of_is_visible_but_nothing_after_it_is(self):
        as_of = pd.Timestamp("2026-06-15T12:00:00Z")
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=1, freq="h")

        index = pd.date_range(as_of - pd.Timedelta(hours=200), as_of, freq="h")
        price = pd.Series(np.arange(len(index), dtype=float), index=index)

        one_hour_later = pd.Series([999_999.0], index=[as_of + pd.Timedelta(hours=1)])
        combined = pd.concat([price, one_hour_later])

        features = build_features(combined, as_of, target_times)

        # lag_24 reads as_of - 24h, which is within the visible series -- must equal the
        # real value there, not the sentinel from the future row.
        assert features.loc[0, "lag_24"] == price[as_of - pd.Timedelta(hours=24)]
        assert 999_999.0 not in features.to_numpy()

    def test_holds_for_many_random_historic_as_of_values(self):
        # docs/FORECASTING.md §5's own wording: "for a set of random historic as_of
        # values, asserts no feature row references data with a later observation time."
        rng = random.Random(20260615)
        series_start = pd.Timestamp("2026-01-01T00:00:00Z")
        price = _synthetic_price_series(series_start, hours=24 * 120)  # ~4 months

        earliest_as_of = series_start + pd.Timedelta(hours=200)
        latest_as_of = price.index[-1] - pd.Timedelta(hours=24)

        for _ in range(100):
            offset_hours = rng.uniform(0, (latest_as_of - earliest_as_of).total_seconds() / 3600.0)
            as_of = earliest_as_of + pd.Timedelta(hours=offset_hours)
            target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=24, freq="h")

            features = build_features(price, as_of, target_times)

            # Independently recompute the expected lag values from scratch, using only
            # data <= as_of, and compare -- a correctness check, not just "didn't crash".
            visible = price[price.index <= as_of]
            expected_lag_24 = visible.reindex([as_of - pd.Timedelta(hours=24)]).iloc[0]
            expected_lag_168 = visible.reindex([as_of - pd.Timedelta(hours=168)]).iloc[0]

            assert (features["lag_24"] == expected_lag_24).all() or (
                pd.isna(features["lag_24"]).all() and pd.isna(expected_lag_24)
            )
            assert (features["lag_168"] == expected_lag_168).all() or (
                pd.isna(features["lag_168"]).all() and pd.isna(expected_lag_168)
            )

    def test_empty_target_times_raises_rather_than_returning_something_meaningless(self):
        as_of = pd.Timestamp("2026-06-15T12:00:00Z")
        price = _synthetic_price_series(as_of - pd.Timedelta(hours=200), 200)

        with pytest.raises(ValueError, match="target_times"):
            build_features(price, as_of, pd.DatetimeIndex([]))


class TestRegimeBD1CurveException:
    """
    Regime B's d1_curve_* features are the one deliberate exception to "never read data
    timestamped after as_of" (features/builder.py's _d1_curve_features): once the
    day-ahead auction clears (Oslo-local as_of >= 13:00), tomorrow's cleared curve is
    genuinely public knowledge even though its hours haven't occurred yet. These tests
    prove the exception is bounded to exactly that one day, not a general future-data leak.
    """

    def test_d1_curve_is_absent_for_a_regime_a_as_of(self):
        as_of = pd.Timestamp("2026-06-15T06:00:00Z")  # 08:00 Oslo -- before publication
        price = _synthetic_price_series(as_of - pd.Timedelta(hours=48), 96)  # spans into "tomorrow"
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=5, freq="h")

        features = build_features(price, as_of, target_times)

        assert "d1_curve_mean" not in features.columns

    def test_d1_curve_reads_exactly_tomorrows_oslo_day_not_the_day_after(self):
        as_of = pd.Timestamp("2026-06-15T12:00:00Z")  # 14:00 Oslo -- Regime B
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=1, freq="h")

        # Two distinct, distinguishable future days: D+1 (the real feature) and D+2 (must
        # never be read for this feature).
        d1_oslo_start = pd.Timestamp("2026-06-16T00:00:00", tz="Europe/Oslo")
        d1_index = pd.date_range(d1_oslo_start, periods=24, freq="h", tz="Europe/Oslo").tz_convert("UTC")
        d1_prices = pd.Series(50.0, index=d1_index)

        d2_oslo_start = pd.Timestamp("2026-06-17T00:00:00", tz="Europe/Oslo")
        d2_index = pd.date_range(d2_oslo_start, periods=24, freq="h", tz="Europe/Oslo").tz_convert("UTC")
        d2_prices = pd.Series(999_999.0, index=d2_index)  # must never leak into d1_curve_*

        price = pd.concat([d1_prices, d2_prices])
        features = build_features(price, as_of, target_times)

        assert features.loc[0, "d1_curve_mean"] == 50.0
        assert features.loc[0, "d1_curve_min"] == 50.0
        assert features.loc[0, "d1_curve_max"] == 50.0

    def test_rows_beyond_d1_never_influence_d1_curve_features(self):
        as_of = pd.Timestamp("2026-06-15T12:00:00Z")  # Regime B
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=1, freq="h")
        price = _synthetic_price_series(as_of - pd.Timedelta(hours=200), 200)

        features_clean = build_features(price, as_of, target_times)

        # Smuggle in data starting two full days after as_of's Oslo day -- past D+1 entirely.
        beyond_d1_start = pd.Timestamp("2026-06-18T00:00:00", tz="Europe/Oslo").tz_convert("UTC")
        beyond_d1_index = pd.date_range(beyond_d1_start, periods=200, freq="h")
        beyond_d1_prices = pd.Series(np.full(len(beyond_d1_index), 999_999.0), index=beyond_d1_index)
        price_with_smuggled_rows = pd.concat([price, beyond_d1_prices])

        features_with_leakage_attempt = build_features(price_with_smuggled_rows, as_of, target_times)

        pd.testing.assert_frame_equal(features_clean, features_with_leakage_attempt)

    def test_d1_curve_boundary_is_correct_across_the_spring_forward_transition(self):
        # docs/ARCHITECTURE.md §3: a DST fixture is mandatory, not optional. Regression
        # test for the same Timedelta-across-DST bug fixed in
        # training.lightgbm_model.issue_times_in_range: _d1_curve_features used to compute
        # D+1's boundary the same buggy way.
        as_of = pd.Timestamp("2026-03-28T12:00:00Z")  # 13:00 Oslo (CET) -- Regime B
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=1, freq="h")

        d1_oslo_start = pd.Timestamp("2026-03-29T00:00:00", tz="Europe/Oslo")  # the transition day itself
        d1_index = pd.date_range(d1_oslo_start, periods=23, freq="h", tz="Europe/Oslo").tz_convert("UTC")
        price = pd.Series(60.0, index=d1_index)

        features = build_features(price, as_of, target_times)

        assert features.loc[0, "d1_curve_mean"] == 60.0


class TestYesterdaySliceDstBoundary:
    def test_yesterday_boundary_is_correct_across_the_fall_back_transition(self):
        as_of = pd.Timestamp("2026-10-26T11:00:00Z")  # 12:00 Oslo (CET), the day after fall-back
        yesterday_oslo_start = pd.Timestamp("2026-10-25T00:00:00", tz="Europe/Oslo")  # the transition day
        yesterday_index = pd.date_range(yesterday_oslo_start, periods=25, freq="h", tz="Europe/Oslo").tz_convert("UTC")
        price = pd.Series(70.0, index=yesterday_index)
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=1, freq="h")

        features = build_features(price, as_of, target_times)

        assert features.loc[0, "yesterday_mean"] == 70.0

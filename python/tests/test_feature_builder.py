from __future__ import annotations

import numpy as np
import pandas as pd

from spotlys_model.features.builder import build_features
from spotlys_model.features.manifest import (
    PRICE_LAG_FEATURES,
    ROLLING_FEATURES,
    YESTERDAY_FEATURES,
)


def _series(start: pd.Timestamp, hours: int) -> pd.Series:
    index = pd.date_range(start, periods=hours, freq="h")
    return pd.Series(np.arange(hours, dtype=float), index=index)


class TestBuildFeaturesShape:
    def test_returns_one_row_per_target_time_with_every_regime_a_feature_present(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")  # 02:00 Oslo -- Regime A
        price = _series(as_of - pd.Timedelta(hours=800), 800)
        target_times = pd.date_range(as_of + pd.Timedelta(hours=1), periods=48, freq="h")

        result = build_features(price, as_of, target_times)

        assert len(result) == 48
        expected_columns = (
            {"target_time", "as_of", "lead_hours", "hour_sin", "hour_cos", "day_of_week", "is_weekend"}
            | {spec.name for spec in PRICE_LAG_FEATURES}
            | {spec.name for spec in ROLLING_FEATURES}
            | {spec.name for spec in YESTERDAY_FEATURES}
        )
        assert set(result.columns) == expected_columns

    def test_lead_hours_matches_the_gap_between_as_of_and_target(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(hours=200), 200)
        target_times = pd.DatetimeIndex([as_of + pd.Timedelta(hours=5), as_of + pd.Timedelta(hours=100)])

        result = build_features(price, as_of, target_times)

        assert result["lead_hours"].tolist() == [5.0, 100.0]

    def test_is_weekend_is_true_only_for_saturday_and_sunday(self):
        as_of = pd.Timestamp("2026-06-10T00:00:00Z")  # a Wednesday
        price = _series(as_of - pd.Timedelta(hours=200), 200)
        # 2026-06-13 is a Saturday, 2026-06-15 is a Monday.
        target_times = pd.DatetimeIndex(
            [pd.Timestamp("2026-06-13T12:00:00Z"), pd.Timestamp("2026-06-15T12:00:00Z")]
        )

        result = build_features(price, as_of, target_times)

        assert result["is_weekend"].tolist() == [1, 0]

    def test_missing_lag_data_becomes_nan_not_a_fabricated_zero(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        # Only 10 hours of history -- far short of the 168h lag_168 needs.
        price = _series(as_of - pd.Timedelta(hours=10), 10)
        target_times = pd.DatetimeIndex([as_of + pd.Timedelta(hours=1)])

        result = build_features(price, as_of, target_times)

        assert np.isnan(result.loc[0, "lag_168"])


class TestRollingFeatures:
    def test_rolling_mean_24_matches_a_manual_computation(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        price = _series(as_of - pd.Timedelta(hours=800), 800)
        target_times = pd.DatetimeIndex([as_of + pd.Timedelta(hours=1)])

        result = build_features(price, as_of, target_times)

        window = price[(price.index > as_of - pd.Timedelta(hours=24)) & (price.index <= as_of)]
        assert result.loc[0, "rolling_mean_24"] == window.mean()
        assert result.loc[0, "rolling_std_24"] == window.std()
        assert result.loc[0, "rolling_min_24"] == window.min()
        assert result.loc[0, "rolling_max_24"] == window.max()

    def test_rolling_window_with_no_data_is_nan(self):
        as_of = pd.Timestamp("2026-06-15T00:00:00Z")
        # All 10 hours of history are older than the 720h window itself, so the window has
        # zero data points -- not just a short one.
        price = _series(as_of - pd.Timedelta(hours=800), 10)
        target_times = pd.DatetimeIndex([as_of + pd.Timedelta(hours=1)])

        result = build_features(price, as_of, target_times)

        assert np.isnan(result.loc[0, "rolling_mean_720"])


class TestYesterdayFeatures:
    def test_matches_a_manual_computation_for_the_oslo_calendar_day_before_as_of(self):
        as_of = pd.Timestamp("2026-06-15T10:00:00Z")  # 12:00 Oslo
        price = _series(as_of - pd.Timedelta(hours=800), 800)
        target_times = pd.DatetimeIndex([as_of + pd.Timedelta(hours=1)])

        result = build_features(price, as_of, target_times)

        oslo_as_of = as_of.tz_convert("Europe/Oslo")
        yesterday_start = (oslo_as_of.normalize() - pd.Timedelta(days=1)).tz_convert("UTC")
        yesterday_end = yesterday_start + pd.Timedelta(days=1)
        yesterday = price[(price.index >= yesterday_start) & (price.index < yesterday_end)]

        assert result.loc[0, "yesterday_mean"] == yesterday.mean()
        assert result.loc[0, "yesterday_spread"] == yesterday.max() - yesterday.min()
        assert result.loc[0, "yesterday_peak_hour"] == float(yesterday.idxmax().tz_convert("Europe/Oslo").hour)

from __future__ import annotations

import numpy as np
import pandas as pd

from spotlys_model.training.backtest import Fold
from spotlys_model.training.lightgbm_model import (
    QUANTILES,
    REGIME_A_FEATURE_COLUMNS,
    build_training_frame,
    issue_times_in_fold,
    pinball_loss,
    predict_quantiles,
    run_lightgbm_backtest,
    summarize_quantile_model,
    target_times_for_regime,
    train_quantile_models,
)


def _synthetic_price_series(start: pd.Timestamp, hours: int, negative: bool = False) -> pd.Series:
    index = pd.date_range(start, periods=hours, freq="h")
    base = np.sin(np.arange(hours) / 24 * 2 * np.pi) * 30 + 50
    if negative:
        base = base - 100  # pushes below the real backfilled data's own -70.93 minimum
    return pd.Series(base, index=index)


class TestTargetTimesForRegime:
    def test_regime_a_starts_one_hour_after_as_of(self):
        as_of = pd.Timestamp("2026-06-15T06:00:00Z")
        target_times = target_times_for_regime(as_of, "A")

        assert target_times[0] == as_of + pd.Timedelta(hours=1)
        assert len(target_times) == 168

    def test_regime_b_starts_at_the_beginning_of_d_plus_2_oslo_not_d_plus_1(self):
        as_of = pd.Timestamp("2026-06-15T12:00:00Z")  # 14:00 Oslo, Regime B
        target_times = target_times_for_regime(as_of, "B")

        d2_start_oslo = pd.Timestamp("2026-06-17T00:00:00", tz="Europe/Oslo")
        assert target_times[0] == d2_start_oslo.tz_convert("UTC")
        assert len(target_times) == 168

    def test_regime_b_d_plus_2_boundary_is_correct_across_the_fall_back_transition(self):
        # 2026-10-25 is Norway's fall-back day. as_of issued the day before, at 13:15 Oslo
        # (CEST, +2) -- D+2 must still land on real Oslo midnight two days later, not
        # midnight-shifted-by-an-hour the way Timedelta(days=2) would silently produce.
        as_of = pd.Timestamp("2026-10-23T11:15:00Z")  # 13:15 Oslo (CEST)
        target_times = target_times_for_regime(as_of, "B")

        d2_start_oslo = pd.Timestamp("2026-10-25T00:00:00", tz="Europe/Oslo")
        assert target_times[0] == d2_start_oslo.tz_convert("UTC")


class TestIssueTimesInFold:
    def test_regime_a_issues_at_06_00_oslo(self):
        fold = Fold(
            fold_start=pd.Timestamp("2026-06-01T00:00:00Z"),
            fold_end=pd.Timestamp("2026-06-03T00:00:00Z"),
            train_end=pd.Timestamp("2026-05-28T00:00:00Z"),
        )
        issue_times = issue_times_in_fold(fold, "A")

        assert len(issue_times) == 2
        for t in issue_times:
            assert t.tz_convert("Europe/Oslo").hour == 6

    def test_regime_b_issues_at_13_15_oslo(self):
        fold = Fold(
            fold_start=pd.Timestamp("2026-06-01T00:00:00Z"),
            fold_end=pd.Timestamp("2026-06-03T00:00:00Z"),
            train_end=pd.Timestamp("2026-05-28T00:00:00Z"),
        )
        issue_times = issue_times_in_fold(fold, "B")

        for t in issue_times:
            oslo = t.tz_convert("Europe/Oslo")
            assert (oslo.hour, oslo.minute) == (13, 15)

    def test_issue_times_land_on_the_correct_oslo_wall_clock_hour_across_both_dst_transitions(self):
        # docs/ARCHITECTURE.md §3: "A test fixture for each transition date is mandatory."
        # Regression test for a real bug found while running the Phase 3 part 2 backtest:
        # `oslo_midnight + Timedelta(hours=13, minutes=15)` silently lands at 12:15 Oslo on
        # 2022-10-30 (the fall-back day) because Timedelta is a fixed physical duration, not
        # a wall-clock offset -- confirmed live, fixed by stripping tz and re-localizing.
        spring_forward = Fold(  # 2026-03-29: 01:00 -> 03:00 CEST
            fold_start=pd.Timestamp("2026-03-27T00:00:00Z"),
            fold_end=pd.Timestamp("2026-03-31T00:00:00Z"),
            train_end=pd.Timestamp("2026-03-20T00:00:00Z"),
        )
        fall_back = Fold(  # 2026-10-25: 03:00 -> 02:00 CET
            fold_start=pd.Timestamp("2026-10-23T00:00:00Z"),
            fold_end=pd.Timestamp("2026-10-27T00:00:00Z"),
            train_end=pd.Timestamp("2026-10-16T00:00:00Z"),
        )

        for fold in (spring_forward, fall_back):
            for regime, expected in (("A", (6, 0)), ("B", (13, 15))):
                for t in issue_times_in_fold(fold, regime):
                    oslo = t.tz_convert("Europe/Oslo")
                    assert (oslo.hour, oslo.minute) == expected, f"{t} -> {oslo} for regime {regime}"


class TestBuildTrainingFrame:
    def test_includes_regime_b_columns_only_for_regime_b(self):
        # Regime A's own real issue time (06:00 Oslo, before the publication clock) and
        # Regime B's (13:15 Oslo, after it) -- the combination run_lightgbm_backtest's
        # issue_times_in_range actually produces, not an artificial regime/clock mismatch.
        as_of_a = pd.Timestamp("2026-06-10T04:00:00Z")  # 06:00 Oslo (CEST)
        as_of_b = pd.Timestamp("2026-06-10T11:15:00Z")  # 13:15 Oslo (CEST)
        price = _synthetic_price_series(as_of_b - pd.Timedelta(hours=800), 1200)

        frame_a = build_training_frame(price, [as_of_a], "A")
        frame_b = build_training_frame(price, [as_of_b], "B")

        assert "d1_curve_mean" not in frame_a.columns
        assert "d1_curve_mean" in frame_b.columns

    def test_drops_rows_with_no_realised_actual(self):
        # Only a short history, well short of the 168h Regime A horizon -- most target
        # hours have no realised price yet. 810 hourly points starting at as_of-800h end at
        # as_of+9h, so exactly 9 of the 168 target hours (as_of+1..as_of+9) have data.
        as_of = pd.Timestamp("2026-06-10T06:00:00Z")
        price = _synthetic_price_series(as_of - pd.Timedelta(hours=800), 810)

        frame = build_training_frame(price, [as_of], "A")

        assert frame["actual"].notna().all()
        assert len(frame) == 9


class TestTrainAndPredictQuantiles:
    def test_predictions_are_monotonically_non_decreasing_across_quantiles(self):
        as_of = pd.Timestamp("2026-01-01T06:00:00Z")
        price = _synthetic_price_series(as_of - pd.Timedelta(days=60), 24 * 90)

        train_as_of_values = list(issue_times_in_fold(
            Fold(as_of - pd.Timedelta(days=50), as_of, as_of), "A", stride_days=2
        ))
        train_frame = build_training_frame(price, train_as_of_values, "A")
        models = train_quantile_models(train_frame, REGIME_A_FEATURE_COLUMNS, use_log1p=False)

        target_times = target_times_for_regime(as_of, "A")
        feats = _build_features_for_test(price, as_of, target_times)
        predictions = predict_quantiles(models, feats[REGIME_A_FEATURE_COLUMNS], use_log1p=False)

        values = predictions[[f"q{int(t * 100):02d}" for t in QUANTILES]].to_numpy()
        assert (np.diff(values, axis=1) >= 0).all()

    def test_log1p_round_trip_recovers_the_same_scale_as_raw_predictions(self):
        # Not a claim that log1p wins -- just that the transform/inverse-transform path
        # itself is correct (predictions land back in real øre/kWh, not log-space).
        as_of = pd.Timestamp("2026-01-01T06:00:00Z")
        price = _synthetic_price_series(as_of - pd.Timedelta(days=60), 24 * 90)

        train_as_of_values = list(issue_times_in_fold(
            Fold(as_of - pd.Timedelta(days=50), as_of, as_of), "A", stride_days=2
        ))
        train_frame = build_training_frame(price, train_as_of_values, "A")
        models = train_quantile_models(train_frame, REGIME_A_FEATURE_COLUMNS, use_log1p=True)

        target_times = target_times_for_regime(as_of, "A")
        feats = _build_features_for_test(price, as_of, target_times)
        predictions = predict_quantiles(models, feats[REGIME_A_FEATURE_COLUMNS], use_log1p=True)

        # Real prices are roughly in the 0-200 range for this synthetic series; a broken
        # inverse transform would produce values off by orders of magnitude or NaN.
        assert predictions["q50"].between(-100, 300).all()

    def test_trains_and_predicts_without_error_when_prices_are_genuinely_negative(self):
        # docs/FORECASTING.md §3: "Negative prices exist -- the offset must handle them and
        # a unit test must cover a negative-price day." Real NO2 history has a -70.93
        # øre/kWh minimum; this series goes lower still.
        as_of = pd.Timestamp("2026-01-01T06:00:00Z")
        price = _synthetic_price_series(as_of - pd.Timedelta(days=60), 24 * 90, negative=True)
        assert price.min() < -70.93

        train_as_of_values = list(issue_times_in_fold(
            Fold(as_of - pd.Timedelta(days=50), as_of, as_of), "A", stride_days=2
        ))
        train_frame = build_training_frame(price, train_as_of_values, "A")

        for use_log1p in (False, True):
            models = train_quantile_models(train_frame, REGIME_A_FEATURE_COLUMNS, use_log1p)
            target_times = target_times_for_regime(as_of, "A")
            feats = _build_features_for_test(price, as_of, target_times)
            predictions = predict_quantiles(models, feats[REGIME_A_FEATURE_COLUMNS], use_log1p)

            assert predictions.notna().all().all()


class TestRunLightgbmBacktest:
    def test_produces_one_row_per_target_time_per_issue_time_in_the_fold(self):
        start = pd.Timestamp("2026-01-01T00:00:00Z")
        price = _synthetic_price_series(start, 24 * 150)

        fold = Fold(
            fold_start=pd.Timestamp("2026-04-01T00:00:00Z"),
            fold_end=pd.Timestamp("2026-04-03T00:00:00Z"),
            train_end=pd.Timestamp("2026-03-28T00:00:00Z"),
        )
        results = run_lightgbm_backtest(price, [fold], "A", use_log1p=False, train_stride_days=5)

        assert len(results) == 2 * 168
        assert {"as_of", "target_time", "lead_hours", "q05", "q50", "q95", "actual"}.issubset(results.columns)


class TestPinballLoss:
    def test_zero_at_a_perfect_prediction(self):
        actual = pd.Series([100.0, 50.0])
        assert (pinball_loss(actual, actual, tau=0.5) == 0).all()

    def test_under_prediction_is_penalised_by_tau_over_prediction_by_one_minus_tau(self):
        actual = pd.Series([100.0])
        under = pd.Series([80.0])  # predicted below actual
        over = pd.Series([120.0])  # predicted above actual

        tau = 0.9
        assert pinball_loss(actual, under, tau).iloc[0] == tau * 20.0
        assert pinball_loss(actual, over, tau).iloc[0] == (1 - tau) * 20.0


class TestSummarizeQuantileModel:
    def test_mae_and_skill_use_q50_the_same_way_summarize_uses_a_baseline_column(self):
        target_times = pd.date_range("2026-06-01T01:00:00Z", periods=24, freq="h")
        results = pd.DataFrame(
            {
                "as_of": pd.Timestamp("2026-06-01T00:00:00Z"),
                "target_time": target_times,
                "lead_hours": np.arange(1, 25),
                "q05": 90.0,
                "q25": 95.0,
                "q50": 100.0,
                "q75": 105.0,
                "q95": 110.0,
                "actual": 100.0,
            }
        )

        summary = summarize_quantile_model(results, b1_mae_by_bucket={"0-24h": 10.0})
        row = summary[summary["lead_bucket"] == "0-24h"].iloc[0]

        assert row["mae_ore_per_kwh"] == 0.0
        assert row["skill_vs_b1"] == 1.0

    def test_coverage_is_the_fraction_of_actuals_inside_the_interval(self):
        target_times = pd.date_range("2026-06-01T01:00:00Z", periods=4, freq="h")
        results = pd.DataFrame(
            {
                "as_of": pd.Timestamp("2026-06-01T00:00:00Z"),
                "target_time": target_times,
                "lead_hours": [1, 2, 3, 4],
                "q05": [0.0, 0.0, 0.0, 0.0],
                "q25": [40.0, 40.0, 40.0, 40.0],
                "q50": [50.0, 50.0, 50.0, 50.0],
                "q75": [60.0, 60.0, 60.0, 60.0],
                "q95": [100.0, 100.0, 100.0, 100.0],
                # 3 of 4 actuals inside [q25, q75]; all 4 inside [q05, q95].
                "actual": [50.0, 50.0, 50.0, 200.0],
            }
        )

        summary = summarize_quantile_model(results, b1_mae_by_bucket={})
        row = summary[summary["lead_bucket"] == "0-24h"].iloc[0]

        assert row["coverage_50"] == 0.75
        assert row["coverage_90"] == 0.75  # the 200.0 actual is outside [0, 100] too


def _build_features_for_test(price: pd.Series, as_of: pd.Timestamp, target_times: pd.DatetimeIndex) -> pd.DataFrame:
    from spotlys_model.features.builder import build_features

    return build_features(price, as_of, target_times)

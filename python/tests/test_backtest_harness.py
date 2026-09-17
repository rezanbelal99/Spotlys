from __future__ import annotations

import numpy as np
import pandas as pd
import pytest

from spotlys_model.training.backtest import (
    Fold,
    assert_no_fold_trains_on_future_data,
    generate_monthly_folds,
    run_backtest,
    summarize,
)


class TestGenerateMonthlyFolds:
    def test_produces_at_least_the_requested_number_of_folds(self):
        start = pd.Timestamp("2022-09-02T00:00:00Z")
        end = pd.Timestamp("2026-09-17T00:00:00Z")  # ~4 years of real NO2 history

        folds = generate_monthly_folds(start, end, min_folds=24)

        assert len(folds) >= 24

    def test_raises_when_there_is_not_enough_history(self):
        start = pd.Timestamp("2026-01-01T00:00:00Z")
        end = pd.Timestamp("2026-06-01T00:00:00Z")  # 5 months

        with pytest.raises(ValueError, match="only"):
            generate_monthly_folds(start, end, min_folds=24)

    def test_every_fold_has_a_24h_embargo_before_its_evaluated_data(self):
        start = pd.Timestamp("2022-09-02T00:00:00Z")
        end = pd.Timestamp("2026-09-17T00:00:00Z")

        folds = generate_monthly_folds(start, end, min_folds=24)

        assert_no_fold_trains_on_future_data(folds)  # must not raise

    def test_folds_are_contiguous_and_expanding(self):
        start = pd.Timestamp("2022-09-02T00:00:00Z")
        end = pd.Timestamp("2026-09-17T00:00:00Z")

        folds = generate_monthly_folds(start, end, min_folds=24)

        for earlier, later in zip(folds, folds[1:]):
            assert earlier.fold_end == later.fold_start


class TestAssertNoFoldTrainsOnFutureData:
    def test_raises_when_a_fold_has_no_embargo(self):
        broken_fold = Fold(
            fold_start=pd.Timestamp("2026-02-01T00:00:00Z"),
            fold_end=pd.Timestamp("2026-03-01T00:00:00Z"),
            train_end=pd.Timestamp("2026-02-01T00:00:00Z"),  # no gap at all
        )

        with pytest.raises(AssertionError):
            assert_no_fold_trains_on_future_data([broken_fold])

    def test_raises_when_train_end_is_after_fold_start(self):
        broken_fold = Fold(
            fold_start=pd.Timestamp("2026-02-01T00:00:00Z"),
            fold_end=pd.Timestamp("2026-03-01T00:00:00Z"),
            train_end=pd.Timestamp("2026-02-15T00:00:00Z"),  # trains on the fold's own future
        )

        with pytest.raises(AssertionError):
            assert_no_fold_trains_on_future_data([broken_fold])


class TestRunBacktest:
    def test_produces_one_row_per_target_time_per_issue_time_in_the_fold(self):
        start = pd.Timestamp("2026-01-01T00:00:00Z")
        index = pd.date_range(start, periods=24 * 120, freq="h")  # 4 months
        price = pd.Series(np.sin(np.arange(len(index)) / 24) * 50 + 100, index=index)

        fold = Fold(
            fold_start=pd.Timestamp("2026-03-01T00:00:00Z"),
            fold_end=pd.Timestamp("2026-03-03T00:00:00Z"),  # 2 days, small for a fast test
            train_end=pd.Timestamp("2026-02-27T00:00:00Z"),
        )

        results = run_backtest(price, [fold])

        # 2 issue times (one per day) x 168 target hours each.
        assert len(results) == 2 * 168
        assert {"as_of", "target_time", "lead_hours", "b0", "b1", "actual"}.issubset(results.columns)

    def test_never_predicts_using_a_price_from_after_the_fold_train_end_for_b1(self):
        # B1 only ever reads price(target - 168h), which for any target inside the fold is
        # strictly before fold_start > train_end -- so this is really asserting the fold
        # embargo and b1's own as_of-anchoring compose correctly end to end.
        start = pd.Timestamp("2026-01-01T00:00:00Z")
        index = pd.date_range(start, periods=24 * 120, freq="h")
        price = pd.Series(np.arange(len(index), dtype=float), index=index)

        fold = Fold(
            fold_start=pd.Timestamp("2026-03-01T00:00:00Z"),
            fold_end=pd.Timestamp("2026-03-02T00:00:00Z"),
            train_end=pd.Timestamp("2026-02-27T00:00:00Z"),
        )
        results = run_backtest(price, [fold])

        for _, row in results.iterrows():
            expected_b1 = price.get(row["target_time"] - pd.Timedelta(hours=168), np.nan)
            if pd.isna(expected_b1):
                assert pd.isna(row["b1"])
            else:
                assert row["b1"] == expected_b1


class TestSummarize:
    def test_computes_mae_and_skill_against_b1_per_lead_bucket(self):
        # A tiny synthetic result set where b1 is always off by 10 and b0 is always exact,
        # for every row in the 0-24h bucket -- skill for b0 should be a clean 1.0, and for
        # b1 a clean 0.0 (it's being compared against itself).
        target_times = pd.date_range("2026-06-01T01:00:00Z", periods=24, freq="h")
        results = pd.DataFrame(
            {
                "as_of": pd.Timestamp("2026-06-01T00:00:00Z"),
                "target_time": target_times,
                "lead_hours": np.arange(1, 25),
                "b0": 100.0,
                "b1": 110.0,
                "b2": np.nan,
                "b3": np.nan,
                "b4": np.nan,
                "actual": 100.0,
            }
        )

        summary = summarize(results)
        bucket = summary[summary["lead_bucket"] == "0-24h"].set_index("baseline")

        assert bucket.loc["b0", "mae_ore_per_kwh"] == 0.0
        assert bucket.loc["b0", "skill_vs_b1"] == 1.0
        assert bucket.loc["b1", "mae_ore_per_kwh"] == 10.0
        assert bucket.loc["b1", "skill_vs_b1"] == 0.0

    def test_rows_beyond_the_168h_horizon_are_excluded(self):
        results = pd.DataFrame(
            {
                "as_of": pd.Timestamp("2026-06-01T00:00:00Z"),
                "target_time": [pd.Timestamp("2026-06-08T01:00:00Z")],
                "lead_hours": [169.0],  # just past the last bucket's upper bound
                "b0": [100.0],
                "b1": [100.0],
                "b2": [100.0],
                "b3": [100.0],
                "b4": [100.0],
                "actual": [100.0],
            }
        )

        summary = summarize(results)

        assert summary.empty

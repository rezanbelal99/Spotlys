"""
Tests for the ONNX export pipeline (docs/FORECASTING.md §7). No live DB connection --
matches every other test in this suite, and specifically what lets this run in CI without
a Postgres service (the production export run itself, against real data, is a separate,
manual step -- see onnx_export.main()).
"""

from __future__ import annotations

import numpy as np
import pandas as pd
import pytest
from lightgbm import LGBMRegressor

from spotlys_model.export.onnx_export import (
    PARITY_TOLERANCE,
    ParityError,
    check_parity,
    export_to_onnx,
    sha256_of,
)
from spotlys_model.training.backtest import Fold
from spotlys_model.training.lightgbm_model import (
    LGBM_PARAMS,
    QUANTILES,
    REGIME_A_FEATURE_COLUMNS,
    build_training_frame,
    issue_times_in_fold,
    train_quantile_models,
)


def _synthetic_price_series(start: pd.Timestamp, hours: int) -> pd.Series:
    index = pd.date_range(start, periods=hours, freq="h")
    # Realistic øre/kWh magnitude (roughly 100-300, not a toy 0-1 range) *with noise*.
    # A noiseless periodic series creates near-duplicate feature rows sitting exactly on
    # tree split boundaries -- discovered the hard way while building this test: it made
    # LightGBM/ONNX parity look far worse (~0.12 øre/kWh) than the real, noisy production
    # data actually shows, because tiny threshold-rounding differences (see
    # export.onnx_export's module docstring) push degenerate duplicate rows across a split
    # far more often than genuinely varied real prices do.
    rng = np.random.default_rng(20260917)
    values = np.sin(np.arange(hours) / 24 * 2 * np.pi) * 100 + 200 + rng.normal(0, 15, hours)
    return pd.Series(values, index=index)


def _train_small_models() -> tuple[dict[float, LGBMRegressor], pd.DataFrame]:
    as_of = pd.Timestamp("2026-06-15T06:00:00Z")
    price = _synthetic_price_series(as_of - pd.Timedelta(days=200), 24 * 250)
    train_as_of_values = list(
        issue_times_in_fold(Fold(as_of - pd.Timedelta(days=150), as_of, as_of), "A", stride_days=2)
    )
    train_frame = build_training_frame(price, train_as_of_values, "A")
    models = train_quantile_models(train_frame, REGIME_A_FEATURE_COLUMNS, use_log1p=True)
    return models, train_frame


class TestExportParity:
    """The mandatory parity test (docs/FORECASTING.md §7): "run [feature rows] through
    both the Python model and the exported ONNX model and assert max absolute difference"
    stays under tolerance. Uses the project's actual production hyperparameters
    (LGBM_PARAMS, imported directly -- not restated here) and realistic øre/kWh
    magnitudes: a smaller/simpler toy model could pass even if the real one wouldn't."""

    def test_parity_within_tolerance_for_every_quantile(self):
        models, train_frame = _train_small_models()
        sample = train_frame.sample(n=min(500, len(train_frame)), random_state=1)
        x_sample = sample[REGIME_A_FEATURE_COLUMNS]

        for tau in QUANTILES:
            onnx_bytes = export_to_onnx(models[tau], REGIME_A_FEATURE_COLUMNS)
            max_diff = check_parity(models[tau], onnx_bytes, x_sample)
            assert max_diff < PARITY_TOLERANCE

    def test_check_parity_raises_on_a_genuine_mismatch(self):
        # Export one model but check parity against a *different* model's predictions --
        # proves the check actually catches a real disagreement, not just passes trivially
        # regardless of what's compared.
        models, train_frame = _train_small_models()
        mismatched_model = LGBMRegressor(objective="quantile", alpha=0.95, **LGBM_PARAMS)
        mismatched_model.fit(train_frame[REGIME_A_FEATURE_COLUMNS], train_frame["actual"])

        sample = train_frame.sample(n=min(500, len(train_frame)), random_state=1)
        x_sample = sample[REGIME_A_FEATURE_COLUMNS]
        onnx_bytes = export_to_onnx(models[0.05], REGIME_A_FEATURE_COLUMNS)

        with pytest.raises(ParityError):
            check_parity(mismatched_model, onnx_bytes, x_sample)


class TestSha256Of:
    def test_deterministic_and_content_sensitive(self):
        assert sha256_of(b"abc") == sha256_of(b"abc")
        assert sha256_of(b"abc") != sha256_of(b"abd")

"""
ONNX export (docs/FORECASTING.md §7) for the production-candidate quantile models --
trained on the *entire* real price history, not a backtest fold (a fold's models exist
only to be scored and discarded during the walk-forward backtest). Writes `model_version`
rows directly via psycopg (docs/adr/0002: Python trains, .NET serves -- the training/export
pipeline owns writing its own provenance record; .NET only reads it back).

Parity tolerance: FORECASTING.md §7 says "assert max absolute difference < 1e-5". That
isn't achievable here, and it took two rounds of measurement to find out why. The first
measurement (a handful of independent random features) showed ~3.6e-5 with
`DoubleTensorType`, which looked like a comfortably-fixable float32-vs-float64 rounding
gap. Against the *real* feature set (27 correlated lag/rolling/calendar columns, our real
300-tree hyperparameters) the gap turned out to be far larger: max ~0.16 øre/kWh, with
10-14% of sampled rows exceeding even a 1e-3 tolerance. The root cause: `onnxmltools`
1.16.0 can only target opset 15 for LightGBM (opset 17 raises "higher than the converter
support"), which means the classic `ai.onnx.ml.TreeEnsembleRegressor` operator --
whose `nodes_values` (split thresholds) and `target_weights` (leaf values) attributes are
declared as ONNX `floats` (float32) in that operator's own schema, full stop, regardless
of what tensor type the graph's *inputs* are declared as. `DoubleTensorType` only affects
the input/output tensors; it never reaches the tree internals. Correlated, real-world
features push more samples right up against a rounded split threshold, so more of them get
routed to the *wrong branch entirely* -- a discrete jump, not a smooth rounding error.
`DoubleTensorType` is kept anyway (it avoids an extra float64->float32 input-rounding step
on top of the tree's own float32 internals, a small but real improvement), but it is not a
fix for the underlying limitation. Resolved with the user: a 0.5 øre/kWh absolute
tolerance -- comfortably covers the measured worst case (0.164) with margin, still tiny
against real price swings (spot prices routinely move by tens of øre hour to hour), and
still catches a genuinely broken conversion (wrong feature order, wrong model entirely).
"""

from __future__ import annotations

import hashlib
import subprocess
from pathlib import Path

import numpy as np
import pandas as pd
from lightgbm import LGBMRegressor
from onnxmltools.convert import convert_lightgbm
from onnxmltools.convert.common.data_types import DoubleTensorType

from spotlys_model.db import get_connection
from spotlys_model.training.backtest import load_no2_price_series
from spotlys_model.training.lightgbm_model import (
    QUANTILES,
    REGIME_A_FEATURE_COLUMNS,
    REGIME_B_FEATURE_COLUMNS,
    build_training_frame,
    issue_times_in_range,
    train_quantile_models,
)

MODELS_DIR = Path(__file__).resolve().parents[2] / "models"
PARITY_TOLERANCE = 0.5  # ore/kWh absolute -- see module docstring
ONNX_OPSET = 15  # the highest this onnxmltools/onnx pair actually supports -- confirmed
# live; target_opset=17 raises "higher than the ... converter support (15)"


class ParityError(RuntimeError):
    """Raised when an exported ONNX model doesn't match its Python source closely enough.
    An export that doesn't verify itself isn't done."""


def git_sha() -> str:
    result = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        capture_output=True,
        text=True,
        check=True,
        cwd=Path(__file__).parent,
    )
    return result.stdout.strip()


def feature_columns_for_regime(regime: str) -> list[str]:
    return REGIME_B_FEATURE_COLUMNS if regime == "B" else REGIME_A_FEATURE_COLUMNS


def export_to_onnx(model: LGBMRegressor, feature_columns: list[str]) -> bytes:
    onnx_model = convert_lightgbm(
        model,
        initial_types=[("input", DoubleTensorType([None, len(feature_columns)]))],
        target_opset=ONNX_OPSET,
    )
    return onnx_model.SerializeToString()


def check_parity(model: LGBMRegressor, onnx_bytes: bytes, x: pd.DataFrame) -> float:
    """Runs the same rows through the Python model and the exported ONNX model. Returns
    the max absolute difference; raises ParityError if it's not under PARITY_TOLERANCE."""
    import onnxruntime as ort

    session = ort.InferenceSession(onnx_bytes)
    x_values = x.to_numpy(dtype=np.float64)
    onnx_pred = session.run(None, {"input": x_values})[0].flatten()
    python_pred = model.predict(x)  # the DataFrame, not x_values -- keeps column names
    # matched to how the model was fit, avoiding an sklearn feature-name mismatch warning

    max_diff = float(np.max(np.abs(onnx_pred - python_pred)))
    if max_diff >= PARITY_TOLERANCE:
        raise ParityError(
            f"ONNX export parity check failed: max abs diff {max_diff:.2e} ore/kWh "
            f">= tolerance {PARITY_TOLERANCE:.2e}"
        )
    return max_diff


def sha256_of(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def train_final_models(
    price: pd.Series, regime: str, stride_days: int = 1
) -> tuple[dict[float, LGBMRegressor], pd.DataFrame]:
    """Trains on all available real history up to now -- not a backtest fold. Returns the
    trained models plus the training frame itself, so the parity check below can sample
    real rows rather than synthetic ones."""
    feature_columns = feature_columns_for_regime(regime)
    as_of_values = list(
        issue_times_in_range(price.index.min(), price.index.max(), regime, stride_days=stride_days)
    )
    train_frame = build_training_frame(price, as_of_values, regime)
    models = train_quantile_models(train_frame, feature_columns, use_log1p=True)
    return models, train_frame


def export_and_register(price: pd.Series, zone: str, regime: str, stride_days: int = 1) -> list[dict[str, object]]:
    """Trains, exports, verifies, and registers all 5 quantile models for one zone/regime.
    Returns the inserted rows' metadata."""
    feature_columns = feature_columns_for_regime(regime)
    models, train_frame = train_final_models(price, regime, stride_days=stride_days)

    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    sha = git_sha()
    trained_at = pd.Timestamp.now(tz="UTC")
    train_data_to = price.index.max()

    # Held-out real rows for the parity check -- a sample, not the whole training set
    # (which can be hundreds of thousands of rows for the full expanding history); the
    # point is verification, not exhaustive testing.
    sample = train_frame.sample(n=min(1000, len(train_frame)), random_state=20260917)
    x_sample = sample[feature_columns]

    inserted: list[dict[str, object]] = []
    with get_connection() as conn:
        for tau in QUANTILES:
            model = models[tau]
            onnx_bytes = export_to_onnx(model, feature_columns)
            max_diff = check_parity(model, onnx_bytes, x_sample)

            digest = sha256_of(onnx_bytes)
            filename = f"{zone.lower()}-{regime.lower()}-q{int(tau * 100):02d}-{digest[:12]}.onnx"
            path = MODELS_DIR / filename
            path.write_bytes(onnx_bytes)

            with conn.cursor() as cur:
                cur.execute(
                    """
                    INSERT INTO model_version
                        (zone, regime, quantile, trained_at_utc, train_data_to,
                         git_sha, onnx_sha256, onnx_path, is_active)
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, false)
                    RETURNING id
                    """,
                    (
                        zone,
                        regime,
                        tau,
                        trained_at.to_pydatetime(),
                        train_data_to.to_pydatetime(),
                        sha,
                        digest,
                        str(path),
                    ),
                )
                row = cur.fetchone()
                row_id = row[0] if row else None
            conn.commit()

            print(
                f"  q{tau:.2f}: parity max_diff={max_diff:.2e} ore/kWh, "
                f"exported to {path.name}, model_version id={row_id}"
            )
            inserted.append(
                {
                    "id": row_id,
                    "zone": zone,
                    "regime": regime,
                    "quantile": tau,
                    "onnx_path": str(path),
                    "onnx_sha256": digest,
                    "parity_max_diff": max_diff,
                }
            )

    return inserted


def main() -> None:
    price = load_no2_price_series()
    print(f"Loaded {len(price)} NO2 hourly rows, {price.index.min()} .. {price.index.max()}")

    for regime in ("A", "B"):
        print(f"\n=== Regime {regime} ===")
        export_and_register(price, zone="NO2", regime=regime)


if __name__ == "__main__":
    main()

"""
LightGBM quantile models (docs/FORECASTING.md §3), trained and walk-forward evaluated
through the same fold/embargo structure the baselines already ran through
(training.backtest.Fold/generate_monthly_folds/EMBARGO_HOURS -- reused, not redefined).
"""

from __future__ import annotations

import numpy as np
import pandas as pd
from lightgbm import LGBMRegressor

from spotlys_model.features.builder import OSLO_TZ, build_features
from spotlys_model.features.manifest import (
    CALENDAR_FEATURES,
    PRICE_LAG_FEATURES,
    REGIME_B_FEATURES,
    ROLLING_FEATURES,
    YESTERDAY_FEATURES,
)
from spotlys_model.training.backtest import LEAD_BUCKETS, Fold, lead_bucket

QUANTILES = [0.05, 0.25, 0.50, 0.75, 0.95]

# Grounded in the real backfilled NO2 history's actual minimum, -70.93 ore/kWh (confirmed
# live from Postgres: 462 negative hours out of 35,448) -- 100 gives headroom above that
# magnitude so log1p(price + offset) stays positive for every observed price, without
# inventing a round number disconnected from real data.
LOG1P_OFFSET = 100.0

REGIME_A_FEATURE_COLUMNS: list[str] = (
    [spec.name for spec in CALENDAR_FEATURES]
    + [spec.name for spec in PRICE_LAG_FEATURES]
    + [spec.name for spec in ROLLING_FEATURES]
    + [spec.name for spec in YESTERDAY_FEATURES]
)
REGIME_B_FEATURE_COLUMNS: list[str] = REGIME_A_FEATURE_COLUMNS + [spec.name for spec in REGIME_B_FEATURES]

# Phase 0's proven starting point (spike/forecast_baseline.ipynb), not re-tuned here.
LGBM_PARAMS: dict[str, object] = {
    "n_estimators": 300,
    "learning_rate": 0.05,
    "num_leaves": 15,
    "min_child_samples": 20,
    "verbosity": -1,
}


def issue_times_in_range(start: pd.Timestamp, end: pd.Timestamp, regime: str, stride_days: int = 1) -> pd.DatetimeIndex:
    """Issue times in [start, end) at the regime's real Oslo-local issue time (06:00 for A,
    13:15 for B -- docs/FORECASTING.md §1), every `stride_days` days.

    Adding an hour/minute `Timedelta` to a tz-aware Oslo timestamp does NOT preserve
    wall-clock time across a DST transition -- confirmed live during Phase 3 planning: on
    2022-10-30 (Norway's fall-back day), `midnight_oslo + Timedelta(hours=13, minutes=15)`
    lands at 12:15 local, not 13:15 (`pd.DateOffset` with hour/minute components has the
    same bug). Stripping the tz, doing the arithmetic on naive values, then re-localizing
    is the only approach that's actually DST-correct.
    """
    oslo_days = pd.date_range(
        start.tz_convert(OSLO_TZ).normalize(),
        end.tz_convert(OSLO_TZ).normalize(),
        freq=f"{stride_days}D",
        inclusive="left",
    )
    issue_offset = pd.Timedelta(hours=6) if regime == "A" else pd.Timedelta(hours=13, minutes=15)
    naive_issue_times = oslo_days.tz_localize(None) + issue_offset
    return naive_issue_times.tz_localize(OSLO_TZ).tz_convert("UTC")


def issue_times_in_fold(fold: Fold, regime: str, stride_days: int = 1) -> pd.DatetimeIndex:
    return issue_times_in_range(fold.fold_start, fold.fold_end, regime, stride_days)


def target_times_for_regime(as_of: pd.Timestamp, regime: str) -> pd.DatetimeIndex:
    """The 168h horizon this regime forecasts (docs/FORECASTING.md §1): D+1..D+7 for A,
    D+2..D+8 for B. B deliberately skips D+1 -- it's already known (the d1_curve feature
    *is* that knowledge, not something left for the model to predict), so evaluating B on
    D+1 would just measure how well it reads its own feature back."""
    if regime == "A":
        start = as_of + pd.Timedelta(hours=1)
    elif regime == "B":
        # DateOffset(days=N), unlike Timedelta(days=N), preserves wall-clock midnight
        # across a DST transition (confirmed live: DateOffset(days=1) on Oslo midnight
        # 2022-10-30 correctly gives 2022-10-31 00:00 local, where Timedelta gives 23:00).
        start = (as_of.tz_convert(OSLO_TZ).normalize() + pd.DateOffset(days=2)).tz_convert("UTC")
    else:
        raise ValueError(f"unknown regime {regime!r}, expected 'A' or 'B'")
    return pd.date_range(start, periods=168, freq="h")


def build_training_frame(price: pd.Series, as_of_values: list[pd.Timestamp], regime: str) -> pd.DataFrame:
    """One row per (as_of, target_time) with the realised actual as the label -- built
    through the exact same build_features every prediction goes through, so there is no
    separate training feature path to drift out of sync with (docs/FORECASTING.md §4's
    "feature hygiene": one code path, shared by training and serving)."""
    frames = []
    for as_of in as_of_values:
        target_times = target_times_for_regime(as_of, regime)
        feats = build_features(price, as_of, target_times)
        feats["actual"] = price.reindex(target_times).to_numpy()
        frames.append(feats)
    frame = pd.concat(frames, ignore_index=True)
    return frame.dropna(subset=["actual"])


def train_quantile_models(
    train_frame: pd.DataFrame, feature_columns: list[str], use_log1p: bool
) -> dict[float, LGBMRegressor]:
    x = train_frame[feature_columns]
    y = train_frame["actual"]
    target = np.log1p(y + LOG1P_OFFSET) if use_log1p else y

    models: dict[float, LGBMRegressor] = {}
    for tau in QUANTILES:
        model = LGBMRegressor(objective="quantile", alpha=tau, **LGBM_PARAMS)
        model.fit(x, target)
        models[tau] = model
    return models


def predict_quantiles(models: dict[float, LGBMRegressor], x: pd.DataFrame, use_log1p: bool) -> pd.DataFrame:
    """Predicts all five quantiles and sorts them per row so a lower quantile's prediction
    never exceeds a higher one's (docs/FORECASTING.md §3: "quantile crossing is real and
    looks broken in a chart -- enforce monotonicity post-hoc")."""
    raw = []
    for tau in QUANTILES:
        pred = models[tau].predict(x)
        if use_log1p:
            pred = np.expm1(pred) - LOG1P_OFFSET
        raw.append(pred)

    sorted_stacked = np.sort(np.vstack(raw), axis=0)
    return pd.DataFrame({f"q{int(tau * 100):02d}": sorted_stacked[i] for i, tau in enumerate(QUANTILES)})


def run_lightgbm_backtest(
    price: pd.Series,
    folds: list[Fold],
    regime: str,
    use_log1p: bool,
    train_stride_days: int = 1,
) -> pd.DataFrame:
    """Walk-forward: for each fold, train on an expanding window of historical issue times
    up to fold.train_end (never the fold's own future -- the same embargo every baseline
    already respects), then predict every issue time inside the fold."""
    feature_columns = REGIME_B_FEATURE_COLUMNS if regime == "B" else REGIME_A_FEATURE_COLUMNS
    series_start = price.index.min()
    rows = []

    for fold in folds:
        train_as_of_values = list(
            issue_times_in_range(series_start, fold.train_end, regime, stride_days=train_stride_days)
        )
        if not train_as_of_values:
            continue
        train_frame = build_training_frame(price, train_as_of_values, regime)
        if len(train_frame) == 0:
            continue
        models = train_quantile_models(train_frame, feature_columns, use_log1p)

        for as_of in issue_times_in_fold(fold, regime):
            target_times = target_times_for_regime(as_of, regime)
            feats = build_features(price, as_of, target_times)
            predictions = predict_quantiles(models, feats[feature_columns], use_log1p)
            predictions.insert(0, "target_time", feats["target_time"].to_numpy())
            predictions.insert(0, "as_of", as_of)
            predictions["lead_hours"] = feats["lead_hours"].to_numpy()
            predictions["actual"] = price.reindex(target_times).to_numpy()
            rows.append(predictions)

    if not rows:
        return pd.DataFrame(columns=["as_of", "target_time", "lead_hours", "q05", "q25", "q50", "q75", "q95", "actual"])
    return pd.concat(rows, ignore_index=True)


def pinball_loss(actual: pd.Series, predicted: pd.Series, tau: float) -> pd.Series:
    """docs/FORECASTING.md §6: "Pinball loss averaged over the five quantiles -- the real
    objective." Penalises under- and over-prediction asymmetrically by tau."""
    diff = actual - predicted
    return np.maximum(tau * diff, (tau - 1) * diff)


def summarize_quantile_model(results: pd.DataFrame, b1_mae_by_bucket: dict[str, float]) -> pd.DataFrame:
    """
    Per lead bucket: MAE/skill for q50 (directly comparable to every baseline's own MAE,
    against the *same* B1 MAE the baseline report used -- skill is a bucket-level ratio, so
    it doesn't require row-for-row alignment with the baseline sample), mean pinball loss
    across all five quantiles, and 50%/90% interval coverage (docs/FORECASTING.md §6:
    "A 90% band that contains 62% of outcomes is a broken band").
    """
    results = results.copy()
    results["lead_bucket"] = results["lead_hours"].apply(lead_bucket)
    results = results[results["lead_bucket"].notna()]

    rows = []
    for bucket, bucket_rows in results.groupby("lead_bucket"):
        mae = (bucket_rows["q50"] - bucket_rows["actual"]).abs().mean()
        b1_mae = b1_mae_by_bucket.get(bucket)
        skill = 1 - mae / b1_mae if b1_mae and pd.notna(mae) else float("nan")

        pinball = float(
            np.mean(
                [pinball_loss(bucket_rows["actual"], bucket_rows[f"q{int(tau * 100):02d}"], tau).mean() for tau in QUANTILES]
            )
        )
        coverage_50 = ((bucket_rows["actual"] >= bucket_rows["q25"]) & (bucket_rows["actual"] <= bucket_rows["q75"])).mean()
        coverage_90 = ((bucket_rows["actual"] >= bucket_rows["q05"]) & (bucket_rows["actual"] <= bucket_rows["q95"])).mean()

        rows.append(
            {
                "lead_bucket": bucket,
                "n": len(bucket_rows),
                "mae_ore_per_kwh": mae,
                "skill_vs_b1": skill,
                "pinball_loss": pinball,
                "coverage_50": coverage_50,
                "coverage_90": coverage_90,
            }
        )

    summary = pd.DataFrame(
        rows, columns=["lead_bucket", "n", "mae_ore_per_kwh", "skill_vs_b1", "pinball_loss", "coverage_50", "coverage_90"]
    )
    bucket_order = {f"{low}-{high}h": i for i, (low, high) in enumerate(LEAD_BUCKETS)}
    summary["_order"] = summary["lead_bucket"].map(bucket_order)
    return summary.sort_values("_order").drop(columns="_order").reset_index(drop=True)

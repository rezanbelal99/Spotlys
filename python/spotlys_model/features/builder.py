"""
The shared feature builder (docs/FORECASTING.md §7): the one function training and
evaluation both call, so training/serving skew can't creep in through two copies of the
same logic drifting apart.

Point-in-time correctness (docs/FORECASTING.md §5) lives here, defensively: every internal
read of `price` is filtered to `price.index <= as_of` before use, regardless of what the
caller passes in. A caller that hands this function its *entire* history (including rows
after `as_of`) gets exactly the same output as one that pre-truncates -- the guard is a
property of this function, not a contract the caller has to uphold correctly every time.

The one deliberate exception is Regime B's D+1 curve (see `_d1_curve_features`), which by
its very nature reads rows timestamped after `as_of` -- gated narrowly behind the real
day-ahead publication clock, not the generic as_of filter, and documented there.
"""

from __future__ import annotations

import numpy as np
import pandas as pd

from spotlys_model.features.manifest import (
    PRICE_LAG_FEATURES,
    REGIME_B_FEATURES,
    ROLLING_STATS,
    ROLLING_WINDOWS_HOURS,
    FeatureSpec,
)

OSLO_TZ = "Europe/Oslo"
REGIME_B_PUBLICATION_HOUR_OSLO = 13  # docs/DOMAIN.md §2: tomorrow's prices from ~13:00


class PointInTimeViolation(RuntimeError):
    """Raised when a feature would need data not yet knowable at as_of."""


def _assert_available(spec: FeatureSpec, source_time: pd.Timestamp, as_of: pd.Timestamp) -> None:
    available_at = source_time + pd.Timedelta(hours=spec.availability_lag_hours)
    if available_at > as_of:
        raise PointInTimeViolation(
            f"feature '{spec.name}' would read data timestamped {source_time} "
            f"(available at {available_at}) for a forecast issued at {as_of}"
        )


def _calendar_features(target_times: pd.DatetimeIndex) -> pd.DataFrame:
    hour_of_day = target_times.hour + target_times.minute / 60.0
    return pd.DataFrame(
        {
            "hour_sin": np.sin(2 * np.pi * hour_of_day / 24),
            "hour_cos": np.cos(2 * np.pi * hour_of_day / 24),
            "day_of_week": target_times.dayofweek,
            "is_weekend": (target_times.dayofweek >= 5).astype(int),
        },
        index=target_times,
    )


def is_regime_b(as_of: pd.Timestamp) -> bool:
    """docs/FORECASTING.md §1: Regime A issues before the auction clears, Regime B after."""
    return as_of.tz_convert(OSLO_TZ).hour >= REGIME_B_PUBLICATION_HOUR_OSLO


def _yesterday_slice(visible: pd.Series, as_of: pd.Timestamp) -> pd.Series:
    """The Oslo calendar day before as_of's own day.

    Note: baselines.b4_shape_times_level's own "yesterday" is actually a UTC calendar day
    (it normalizes as_of without converting to Oslo first) -- a pre-existing discrepancy
    from part 1, discovered while building this function, not something this function
    repeats. Flagged to the user rather than silently changed, since B4's already-reported
    backtest numbers would shift if fixed.

    Uses DateOffset(days=1), not Timedelta(days=1): the latter does not preserve
    wall-clock midnight across a DST transition (confirmed live -- see
    training.lightgbm_model.issue_times_in_range's docstring for the reproduction)."""
    oslo_as_of = as_of.tz_convert(OSLO_TZ)
    yesterday_start_oslo = oslo_as_of.normalize() - pd.DateOffset(days=1)
    yesterday_end_oslo = yesterday_start_oslo + pd.DateOffset(days=1)
    yesterday_start_utc = yesterday_start_oslo.tz_convert("UTC")
    yesterday_end_utc = yesterday_end_oslo.tz_convert("UTC")
    return visible[(visible.index >= yesterday_start_utc) & (visible.index < yesterday_end_utc)]


def _yesterday_features(visible: pd.Series, as_of: pd.Timestamp) -> dict[str, float]:
    yesterday = _yesterday_slice(visible, as_of)
    if len(yesterday) == 0:
        return {"yesterday_mean": np.nan, "yesterday_peak_hour": np.nan, "yesterday_spread": np.nan}
    return {
        "yesterday_mean": yesterday.mean(),
        "yesterday_peak_hour": float(yesterday.idxmax().tz_convert(OSLO_TZ).hour),
        "yesterday_spread": yesterday.max() - yesterday.min(),
    }


def _d1_curve_features(price: pd.Series, as_of: pd.Timestamp) -> dict[str, float]:
    """
    Regime B only. Once the day-ahead auction has cleared (Oslo-local as_of >= 13:00,
    docs/DOMAIN.md §2), tomorrow's full 24h price curve is publicly known even though its
    individual hours haven't occurred yet relative to as_of's clock time -- the one place
    in this module where "timestamped after as_of" does not mean "not yet knowable".
    Scoped as narrowly as possible: only as_of's own Oslo-local next calendar day, only
    behind the `is_regime_b` gate, never used to justify reading anything else from the
    future. `price` here is intentionally the *unfiltered* series, not `visible`.
    """
    if not is_regime_b(as_of):
        return {}

    # DateOffset(days=1), not Timedelta(days=1) -- see _yesterday_slice's docstring.
    oslo_as_of = as_of.tz_convert(OSLO_TZ)
    d1_start_oslo = oslo_as_of.normalize() + pd.DateOffset(days=1)
    d1_end_oslo = d1_start_oslo + pd.DateOffset(days=1)
    d1_start_utc = d1_start_oslo.tz_convert("UTC")
    d1_end_utc = d1_end_oslo.tz_convert("UTC")

    curve = price[(price.index >= d1_start_utc) & (price.index < d1_end_utc)]
    if len(curve) == 0:
        return {"d1_curve_mean": np.nan, "d1_curve_min": np.nan, "d1_curve_max": np.nan, "d1_curve_std": np.nan}
    return {
        "d1_curve_mean": curve.mean(),
        "d1_curve_min": curve.min(),
        "d1_curve_max": curve.max(),
        "d1_curve_std": curve.std(),
    }


def build_features(price: pd.Series, as_of: pd.Timestamp, target_times: pd.DatetimeIndex) -> pd.DataFrame:
    """
    One row per entry in `target_times`, built as of `as_of` -- matches
    docs/FORECASTING.md §6's harness pseudocode (`features = build_features(as_of=T)`).

    `price` should be indexed by hour_start (UTC), ex-VAT øre/kWh, tz-aware. It may be the
    full historical series; see the module docstring for why that's safe.

    Columns: as_of, target_time, lead_hours, calendar (4), price lags (4), rolling stats
    (4 windows x 4 stats), yesterday (3), and -- only when `as_of` falls in Regime B -- the
    four d1_curve_* columns. Regime A rows never have those columns at all.
    """
    if len(target_times) == 0:
        raise ValueError("target_times must not be empty")

    visible = price[price.index <= as_of]

    feats = _calendar_features(target_times)

    for spec in PRICE_LAG_FEATURES:
        lag_hours = int(spec.name.removeprefix("lag_"))
        source_time = as_of - pd.Timedelta(hours=lag_hours)
        _assert_available(spec, source_time, as_of)
        feats[spec.name] = visible.reindex([source_time]).iloc[0]

    for hours in ROLLING_WINDOWS_HOURS:
        window = visible[visible.index > as_of - pd.Timedelta(hours=hours)]
        for stat in ROLLING_STATS:
            feats[f"rolling_{stat}_{hours}"] = getattr(window, stat)() if len(window) > 0 else np.nan

    for name, value in _yesterday_features(visible, as_of).items():
        feats[name] = value

    d1_features = _d1_curve_features(price, as_of)
    for spec in REGIME_B_FEATURES:
        if spec.name in d1_features:
            feats[spec.name] = d1_features[spec.name]

    feats.insert(0, "lead_hours", (target_times - as_of).total_seconds() / 3600.0)
    feats.insert(0, "as_of", as_of)

    return feats.reset_index().rename(columns={"index": "target_time"})

"""
Baselines B0-B4 (docs/FORECASTING.md §2) -- "You do not get to claim a model is good. You
get to claim it beats these, by this much, on this period."

Every baseline here is anchored to `as_of`, not to the target hour, and never reads data
timestamped after `as_of` -- the same point-in-time discipline as the feature builder
(docs/FORECASTING.md §5). This is a deliberate, stated interpretation of the doc's literal
"p(h-24)" notation: taken literally, B0's "yesterday same hour" and a naive reading of B3
would need data from *after* as_of at lead times beyond 24h, which is exactly the kind of
leak this phase exists to prevent. B0 is therefore genuinely only defined for its natural
short-horizon regime (documented per-function) and returns NaN outside it, rather than
silently reading the future. B1, B2, B3, and B4 are redefined relative to as_of so they
stay valid across the full 0-168h horizon -- B1's "same hour, 7 days ago" already was
as_of-safe by construction as long as lead <= 168h, which is the harness's own max horizon.
"""

from __future__ import annotations

import numpy as np
import pandas as pd

from spotlys_model.features.builder import OSLO_TZ


def b0_last_value(price: pd.Series, as_of: pd.Timestamp, target_times: pd.DatetimeIndex) -> pd.Series:
    """p_hat(h) = p(h - 24h). Only defined where h - 24h <= as_of (lead <= 24h) -- this is
    a genuinely short-horizon-only baseline, not a general-purpose one; NaN elsewhere."""
    visible = price[price.index <= as_of]
    source_times = target_times - pd.Timedelta(hours=24)
    values = visible.reindex(source_times).to_numpy()
    lead_hours = (target_times - as_of).total_seconds() / 3600.0
    values = np.where(lead_hours <= 24.0, values, np.nan)
    return pd.Series(values, index=target_times, name="b0")


def b1_seasonal_naive(price: pd.Series, as_of: pd.Timestamp, target_times: pd.DatetimeIndex) -> pd.Series:
    """p_hat(h) = p(h - 168h) -- the one to beat. Valid for the harness's full 0-168h
    horizon: h - 168h <= as_of holds for any h <= as_of + 168h."""
    visible = price[price.index <= as_of]
    source_times = target_times - pd.Timedelta(hours=168)
    values = visible.reindex(source_times).to_numpy()
    return pd.Series(values, index=target_times, name="b1")


def b2_climatology(price: pd.Series, as_of: pd.Timestamp, target_times: pd.DatetimeIndex) -> pd.Series:
    """Median of the same hour-of-week over the trailing 28 days *ending at as_of* (not the
    target hour), so it's valid at any lead time -- a real forecaster's climatology is
    built from what they know now, not from a window that moves with the horizon."""
    visible = price[(price.index <= as_of) & (price.index > as_of - pd.Timedelta(days=28))]
    by_hour_of_week = visible.groupby([visible.index.dayofweek, visible.index.hour]).median()

    values = [
        by_hour_of_week.get((t.dayofweek, t.hour), np.nan) for t in target_times
    ]
    return pd.Series(values, index=target_times, name="b2")


def b3_damped_persistence(
    price: pd.Series, as_of: pd.Timestamp, target_times: pd.DatetimeIndex, alpha: float
) -> pd.Series:
    """p_hat(h) = alpha * p(as_of) + (1 - alpha) * rolling_mean_168h(as_of) -- a single
    value broadcast across every target hour, exactly like the price-lag features in
    features/builder.py. Blends the most recent known price toward the trailing week's
    average, damped by alpha; valid at any lead since both terms are as_of-anchored."""
    visible = price[price.index <= as_of].sort_index()
    if len(visible) == 0:
        return pd.Series(np.nan, index=target_times, name="b3")

    # `visible` is already filtered to <= as_of and sorted, so its last element is the most
    # recent known price at or before as_of -- whether or not as_of itself has an exact row.
    last_value = visible.iloc[-1]
    window = visible[visible.index > as_of - pd.Timedelta(hours=168)]
    rolling_mean = window.mean()

    value = alpha * last_value + (1 - alpha) * rolling_mean
    return pd.Series(value, index=target_times, name="b3")


def fit_b3_alpha(price: pd.Series, as_of_values: list[pd.Timestamp], target_hours_ahead: int = 24) -> float:
    """Grid search over alpha in [0, 1] minimising MAE on the given training as_of points,
    one hour ahead each (FORECASTING.md doesn't specify a fitting method; this is the
    natural one). Only uses data at or before each as_of."""
    best_alpha = 0.5
    best_mae = np.inf

    for alpha in np.arange(0.0, 1.01, 0.05):
        errors = []
        for as_of in as_of_values:
            target = pd.DatetimeIndex([as_of + pd.Timedelta(hours=target_hours_ahead)])
            pred = b3_damped_persistence(price, as_of, target, alpha).iloc[0]
            actual = price.get(target[0], np.nan)
            if not (np.isnan(pred) or np.isnan(actual)):
                errors.append(abs(pred - actual))

        if errors:
            mae = float(np.mean(errors))
            if mae < best_mae:
                best_mae = mae
                best_alpha = float(alpha)

    return best_alpha


def b4_shape_times_level(price: pd.Series, as_of: pd.Timestamp, target_times: pd.DatetimeIndex) -> pd.Series:
    """
    Yesterday's normalised 24h shape (relative to as_of's own Oslo calendar day) times a
    level proxy. Per the Phase 3 plan: for a *baseline*, not a real level-forecasting
    model, the level proxy is the trailing 7-day mean ending at as_of -- stated plainly as
    this baseline's own construction, not a claim about what a production B4 would use.

    Fixed from an earlier, already-reported version that used UTC calendar days and UTC
    hour-of-day throughout (as_of.normalize() without converting to Oslo first, and
    .hour on UTC-indexed timestamps) -- Norwegian demand and price patterns are anchored
    to Oslo-local clock time (people wake, cook, heat on Oslo time, not UTC), so both the
    "yesterday" boundary and the hour-of-day shape profile need to be Oslo-local to mean
    what the docstring always claimed they meant. Uses DateOffset(days=1), not
    Timedelta(days=1), for the same DST-safety reason as features/builder.py's
    _yesterday_slice/_d1_curve_features.
    """
    visible = price[price.index <= as_of]

    oslo_as_of = as_of.tz_convert(OSLO_TZ)
    yesterday_start_oslo = oslo_as_of.normalize() - pd.DateOffset(days=1)
    yesterday_end_oslo = yesterday_start_oslo + pd.DateOffset(days=1)
    yesterday_start = yesterday_start_oslo.tz_convert("UTC")
    yesterday_end = yesterday_end_oslo.tz_convert("UTC")
    yesterday = visible[(visible.index >= yesterday_start) & (visible.index < yesterday_end)]

    level_window = visible[visible.index > as_of - pd.Timedelta(days=7)]
    level = level_window.mean()

    if len(yesterday) == 0 or yesterday.mean() == 0 or np.isnan(level):
        return pd.Series(np.nan, index=target_times, name="b4")

    yesterday_oslo_hour = yesterday.index.tz_convert(OSLO_TZ).hour
    shape_by_hour = (yesterday / yesterday.mean()).groupby(yesterday_oslo_hour).mean()

    target_oslo_hours = target_times.tz_convert(OSLO_TZ).hour
    values = [shape_by_hour.get(h, np.nan) * level for h in target_oslo_hours]
    return pd.Series(values, index=target_times, name="b4")


def all_baselines(
    price: pd.Series, as_of: pd.Timestamp, target_times: pd.DatetimeIndex, b3_alpha: float
) -> pd.DataFrame:
    """All five baselines for one issue time, as columns b0..b4 alongside the target time
    and lead hours -- the shape python.tests/spotlys_model.training.backtest joins against
    realised prices to score."""
    frame = pd.DataFrame(
        {
            "target_time": target_times,
            "lead_hours": (target_times - as_of).total_seconds() / 3600.0,
            "b0": b0_last_value(price, as_of, target_times).to_numpy(),
            "b1": b1_seasonal_naive(price, as_of, target_times).to_numpy(),
            "b2": b2_climatology(price, as_of, target_times).to_numpy(),
            "b3": b3_damped_persistence(price, as_of, target_times, b3_alpha).to_numpy(),
            "b4": b4_shape_times_level(price, as_of, target_times).to_numpy(),
        }
    )
    frame.insert(0, "as_of", as_of)
    return frame

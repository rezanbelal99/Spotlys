"""Feature availability manifest (docs/FORECASTING.md §4).

Every feature the builder can produce declares, once, how many hours must separate its
underlying data's own timestamp from `as_of` before it may be used for a forecast issued at
that instant. The builder asserts against this for every feature it computes rather than
asking a caller to remember it.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class FeatureSpec:
    name: str
    source: str  # 'calendar' | 'price_observation' | 'weather_forecast' | 'hydrology_observation'
    availability_lag_hours: float
    regime: str  # 'both' | 'b_only' -- Regime B's cleared D+1 curve, absent from Regime A


CALENDAR_FEATURES: list[FeatureSpec] = [
    FeatureSpec("hour_sin", "calendar", 0.0, "both"),
    FeatureSpec("hour_cos", "calendar", 0.0, "both"),
    FeatureSpec("day_of_week", "calendar", 0.0, "both"),
    FeatureSpec("is_weekend", "calendar", 0.0, "both"),
]

# Price lags are computed relative to as_of, not the target hour (docs/FORECASTING.md §4):
# lag_24 = price(as_of - 24h), a single value broadcast across every target hour this as_of
# is forecasting -- that construction is what makes it safe at any lead time by
# definition. availability_lag_hours is 0 here: a realised price is known at its own hour,
# with no further publication delay this project models.
PRICE_LAG_FEATURES: list[FeatureSpec] = [
    FeatureSpec("lag_24", "price_observation", 0.0, "both"),
    FeatureSpec("lag_48", "price_observation", 0.0, "both"),
    FeatureSpec("lag_168", "price_observation", 0.0, "both"),
    FeatureSpec("lag_336", "price_observation", 0.0, "both"),
]

# Rolling stats over a window ending at as_of, not the target hour -- same as_of-anchoring
# as the lags above.
ROLLING_WINDOWS_HOURS: list[int] = [24, 72, 168, 720]
ROLLING_STATS: list[str] = ["mean", "std", "min", "max"]
ROLLING_FEATURES: list[FeatureSpec] = [
    FeatureSpec(f"rolling_{stat}_{hours}", "price_observation", 0.0, "both")
    for hours in ROLLING_WINDOWS_HOURS
    for stat in ROLLING_STATS
]

# "Yesterday" = the Oslo calendar day before as_of's own day, matching
# baselines.b4_shape_times_level's existing definition rather than a second one.
YESTERDAY_FEATURES: list[FeatureSpec] = [
    FeatureSpec("yesterday_mean", "price_observation", 0.0, "both"),
    FeatureSpec("yesterday_peak_hour", "price_observation", 0.0, "both"),
    FeatureSpec("yesterday_spread", "price_observation", 0.0, "both"),
]

# Regime B only (docs/FORECASTING.md §1, §4): once the day-ahead auction has cleared
# (Oslo-local as_of >= 13:00, docs/DOMAIN.md §2), tomorrow's full 24h curve is publicly
# known even though its hours haven't occurred yet -- the one deliberate, narrowly-scoped
# exception to "never read a row timestamped after as_of" in this module. See
# features/builder.py's _d1_curve_features for the actual gate.
REGIME_B_FEATURES: list[FeatureSpec] = [
    FeatureSpec("d1_curve_mean", "price_observation", 0.0, "b_only"),
    FeatureSpec("d1_curve_min", "price_observation", 0.0, "b_only"),
    FeatureSpec("d1_curve_max", "price_observation", 0.0, "b_only"),
    FeatureSpec("d1_curve_std", "price_observation", 0.0, "b_only"),
]

# Declared for governance now (docs/FORECASTING.md §4: "every feature declares its
# availability lag in a manifest"), not yet consumed by the baselines/backtest harness in
# this pass -- B0-B4 are pure functions of price and calendar (docs/FORECASTING.md §2), and
# wiring weather/hydrology into real features is step 5's work, once a model exists to use
# them. Ingestion runs ahead of consumption so real archived-forecast history has time to
# accumulate before then (see the Phase 3 plan's own note on this).
WEATHER_FEATURES: list[FeatureSpec] = [
    FeatureSpec("heating_degree_hours", "weather_forecast", 0.0, "both"),
    FeatureSpec("mean_temp_c", "weather_forecast", 0.0, "both"),
    FeatureSpec("mean_wind_ms", "weather_forecast", 0.0, "both"),
]

HYDROLOGY_FEATURES: list[FeatureSpec] = [
    FeatureSpec("reservoir_fill_fraction", "hydrology_observation", 0.0, "both"),
    FeatureSpec("reservoir_deviation_from_median", "hydrology_observation", 0.0, "both"),
]

ALL_FEATURES: list[FeatureSpec] = (
    CALENDAR_FEATURES
    + PRICE_LAG_FEATURES
    + ROLLING_FEATURES
    + YESTERDAY_FEATURES
    + REGIME_B_FEATURES
    + WEATHER_FEATURES
    + HYDROLOGY_FEATURES
)

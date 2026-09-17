"""
The walk-forward backtest harness (docs/FORECASTING.md §6): monthly folds, expanding
window, 24h embargo, issue-time anchored. `Fold`/`generate_monthly_folds`/`EMBARGO_HOURS`
are shared with `training.lightgbm_model`, which walks the same folds for the real
quantile models -- this module itself only runs the five baselines through them (B0-B4 are
pure functions of price/calendar and need no "training" beyond B3's alpha fit, and were
built and reported before any model, per the phase's own instruction).

Regime B doesn't change any of the baseline formulas (they're issue-time-agnostic), so this
module's own `_issue_times_in_fold`/`ISSUE_HOUR_UTC` only ever exercise Regime A's 06:00
issue time -- `training.lightgbm_model.issue_times_in_range` is the regime-aware version
used for the model.
"""

from __future__ import annotations

from dataclasses import dataclass

import pandas as pd

from spotlys_model.training.baselines import all_baselines, fit_b3_alpha

EMBARGO_HOURS = 24
ISSUE_HOUR_UTC = 6  # Regime A (docs/FORECASTING.md §1)
MAX_LEAD_HOURS = 168
LEAD_BUCKETS = [(0, 24), (24, 48), (48, 96), (96, 168)]
BASELINE_COLUMNS = ["b0", "b1", "b2", "b3", "b4"]


@dataclass(frozen=True)
class Fold:
    """One evaluation month. `train_end` is where B3's alpha may be fit from -- strictly
    before `fold_start` by the embargo, so no fold's fitting ever sees its own future."""

    fold_start: pd.Timestamp
    fold_end: pd.Timestamp  # exclusive
    train_end: pd.Timestamp


def generate_monthly_folds(
    series_start: pd.Timestamp, series_end: pd.Timestamp, min_folds: int = 24
) -> list[Fold]:
    """One fold per calendar month, expanding window, walking forward from the first month
    that leaves at least `min_folds` months of subsequent evaluated history -- or from as
    early as the data allows if there's less than that (docs/FORECASTING.md §6: "at least
    24 months -> ~24 folds"; this project's real history determines the actual count)."""
    month_starts = pd.date_range(series_start.normalize().replace(day=1), series_end, freq="MS", tz="UTC")
    # Need at least one full month of history before the first fold, for lag_168 etc. to
    # have anything to read, plus the embargo.
    usable_starts = [m for m in month_starts if m - pd.Timedelta(hours=EMBARGO_HOURS) > series_start]

    folds = []
    for i, start in enumerate(usable_starts):
        end = usable_starts[i + 1] if i + 1 < len(usable_starts) else series_end
        if end > series_end:
            break
        folds.append(Fold(fold_start=start, fold_end=end, train_end=start - pd.Timedelta(hours=EMBARGO_HOURS)))

    if len(folds) < min_folds:
        raise ValueError(
            f"only {len(folds)} monthly folds available from {series_start} to {series_end}, "
            f"need at least {min_folds} (docs/FORECASTING.md §6)"
        )
    return folds


def _issue_times_in_fold(fold: Fold) -> pd.DatetimeIndex:
    days = pd.date_range(fold.fold_start, fold.fold_end, freq="D", inclusive="left", tz="UTC")
    return days + pd.Timedelta(hours=ISSUE_HOUR_UTC)


def run_backtest(price: pd.Series, folds: list[Fold]) -> pd.DataFrame:
    """
    Long-format results: one row per (as_of, target_time, baseline), with prediction and
    the realised price. `price` may be the full series -- see features/builder.py's
    docstring for why passing more history than a fold needs is safe by construction; the
    embargo and as_of-anchoring inside baselines.py are what actually enforce it.
    """
    rows = []

    for fold in folds:
        train_prices = price[price.index <= fold.train_end]

        # Fit B3's alpha on the 30 issue times before this fold's embargo boundary -- never
        # on the fold itself, matching the walk-forward discipline the fold boundaries
        # already enforce for everything else.
        fit_window_start = fold.train_end - pd.Timedelta(days=60)
        fit_as_of_candidates = list(
            pd.date_range(fit_window_start, fold.train_end, freq="D", tz="UTC") + pd.Timedelta(hours=ISSUE_HOUR_UTC)
        )
        alpha = fit_b3_alpha(train_prices, fit_as_of_candidates[-30:] or [fold.train_end])

        for as_of in _issue_times_in_fold(fold):
            target_times = pd.date_range(
                as_of + pd.Timedelta(hours=1), as_of + pd.Timedelta(hours=MAX_LEAD_HOURS), freq="h"
            )
            predictions = all_baselines(price, as_of, target_times, b3_alpha=alpha)
            predictions["actual"] = price.reindex(target_times).to_numpy()
            rows.append(predictions)

    return pd.concat(rows, ignore_index=True)


def assert_no_fold_trains_on_future_data(folds: list[Fold]) -> None:
    """docs/FORECASTING.md §6: "A test asserts that no fold's training data postdates its
    evaluation data." Structural check on the fold boundaries themselves."""
    for fold in folds:
        if fold.train_end >= fold.fold_start:
            raise AssertionError(f"fold {fold} has train_end >= fold_start -- no embargo")
        if fold.fold_start - fold.train_end < pd.Timedelta(hours=EMBARGO_HOURS):
            raise AssertionError(f"fold {fold} has less than the required {EMBARGO_HOURS}h embargo")


def lead_bucket(lead_hours: float) -> str | None:
    for low, high in LEAD_BUCKETS:
        if low < lead_hours <= high:
            return f"{low}-{high}h"
    return None


def summarize(results: pd.DataFrame) -> pd.DataFrame:
    """MAE and skill-vs-B1 (docs/FORECASTING.md §2: `skill = 1 - MAE_model / MAE_B1`) per
    baseline, per lead bucket."""
    results = results.copy()
    results["lead_bucket"] = results["lead_hours"].apply(lead_bucket)
    results = results[results["lead_bucket"].notna()]

    rows = []
    for bucket, bucket_rows in results.groupby("lead_bucket"):
        b1_mae = (bucket_rows["b1"] - bucket_rows["actual"]).abs().mean()
        for col in BASELINE_COLUMNS:
            errors = (bucket_rows[col] - bucket_rows["actual"]).abs()
            n = errors.notna().sum()
            mae = errors.mean()
            skill = 1 - mae / b1_mae if b1_mae and pd.notna(mae) else float("nan")
            rows.append({"lead_bucket": bucket, "baseline": col, "n": n, "mae_ore_per_kwh": mae, "skill_vs_b1": skill})

    summary = pd.DataFrame(rows, columns=["lead_bucket", "baseline", "n", "mae_ore_per_kwh", "skill_vs_b1"])
    bucket_order = {f"{low}-{high}h": i for i, (low, high) in enumerate(LEAD_BUCKETS)}
    summary["_order"] = summary["lead_bucket"].map(bucket_order)
    return summary.sort_values(["_order", "baseline"]).drop(columns="_order").reset_index(drop=True)


def load_no2_price_series() -> pd.Series:
    from spotlys_model.db import get_connection

    with get_connection() as conn:
        frame = pd.read_sql(
            "SELECT hour_start_utc, price_ex_vat FROM price_observation WHERE zone = 'NO2' ORDER BY hour_start_utc",
            conn,
        )
    frame["hour_start_utc"] = pd.to_datetime(frame["hour_start_utc"], utc=True)
    return frame.set_index("hour_start_utc")["price_ex_vat"].astype(float)


def main() -> None:
    price = load_no2_price_series()
    print(f"Loaded {len(price)} NO2 hourly rows, {price.index.min()} .. {price.index.max()}")

    folds = generate_monthly_folds(price.index.min(), price.index.max(), min_folds=24)
    assert_no_fold_trains_on_future_data(folds)
    print(f"{len(folds)} monthly folds, {folds[0].fold_start.date()} .. {folds[-1].fold_end.date()}")

    results = run_backtest(price, folds)
    summary = summarize(results)

    pd.set_option("display.float_format", lambda x: f"{x:.3f}")
    print()
    print(summary.to_string(index=False))


if __name__ == "__main__":
    main()

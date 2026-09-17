# FORECASTING.md — the modelling core

This is the part of the project that is actually hard. Treat it as an experiment log with a deployment attached, not as "add ML".

---

## 1. The task, stated precisely

> For price zone *z*, at issue time *T*, predict the hourly day-ahead price `p(z, h)` for every hour *h* in the window `[T + lead_min, T + 168h]`, as a **predictive distribution**, not a point.

Two regimes, because of the 13:00 publication clock (DOMAIN.md §2):

| Regime | Issue time | Targets | Notes |
|---|---|---|---|
| **A — pre-auction** | 06:00 daily | D+1 … D+7 | D+1 is genuinely unknown; highest value, hardest |
| **B — post-auction** | 13:15 daily | D+2 … D+8 | D+1 is now ground truth and is *fed in as a feature* |

Regime B is much easier: knowing tomorrow's cleared curve tells you a great deal about the day after. Report them separately. A single blended MAE hides which one you're good at.

**Lead time is a first-class dimension of every metric.** Report MAE by lead bucket: 0–24 h, 24–48 h, 48–96 h, 96–168 h.

---

## 2. Baselines — build these before any model

You do not get to claim a model is good. You get to claim it beats these, by this much, on this period.

| # | Baseline | Definition |
|---|---|---|
| B0 | Last value | `p̂(h) = p(h − 24)` |
| B1 | **Seasonal naive (the one to beat)** | `p̂(h) = p(same hour, 7 days ago)` — captures the weekday/weekend and diurnal shape |
| B2 | Climatology | Median of the same hour-of-week over a trailing 28 days |
| B3 | Damped persistence | `p̂(h) = α·p(h−24) + (1−α)·rolling_mean_168h`, α fitted |
| B4 | Shape × level | Yesterday's normalised 24 h *shape* × a forecast of tomorrow's daily *mean level* |

B4 is worth building properly, because it encodes a real structural insight — the diurnal shape is stable and the level is what moves — and it is a surprisingly strong competitor. If your gradient booster can't beat B4 by a clear margin, the honest write-up says so.

Also record, for context, a **skill score**:

```
skill = 1 − MAE_model / MAE_B1
```

Positive means better than seasonal naive. Publish it per zone, per lead bucket, on the dashboard. Publicly showing where your model is weak is a stronger portfolio signal than a single flattering number.

---

## 3. Model family

**Primary: LightGBM, quantile objective, one model per zone.**

Why gradient boosting rather than a deep sequence model:
- Tabular features with strong calendar structure and modest data volume (~35 000 hourly rows per zone since Sept 2022). A transformer will overfit and you will not be able to explain it in an interview.
- Native handling of missing values — real weather and hydrology feeds have gaps.
- Fast enough to retrain nightly on a laptop; fast enough to backtest 50 folds in minutes.
- SHAP values give per-forecast explanations, which the UI uses ("this Thursday is expensive mainly because wind in NO2 collapses").

Why per-zone models rather than one model with a zone feature: the zones have genuinely different regimes (NO4 is export-constrained and structurally cheap; NO2 is coupled to the continent). Start per-zone; test a pooled model with zone embeddings as an experiment and keep whichever wins on the backtest. Record the result in an ADR either way.

**Uncertainty: quantile regression.** Train separate boosters for τ ∈ {0.05, 0.25, 0.50, 0.75, 0.95} with LightGBM's `objective="quantile"`. Enforce monotonicity post-hoc by sorting the predicted quantiles per hour (quantile crossing is real and looks broken in a chart).

**Target transform.** Norwegian prices are right-skewed with occasional near-zero and negative hours. Train on `log1p(price + offset)` for the median model and evaluate in original units; check whether it helps on the backtest rather than assuming. Negative prices exist — the offset must handle them and a unit test must cover a negative-price day.

**Stretch (Phase 5):** a small global model across all five zones plus SE3/SE4/DK1 with zone embeddings, or a probabilistic sequence model (DeepAR-style). Only after the baseline story is complete.

---

## 4. Features

Grouped by how the feature behaves, because that determines its leakage rules.

### Calendar (known perfectly, any horizon)
`hour_of_day` (encoded as sin/cos pair), `day_of_week`, `is_weekend`, `is_public_holiday`, `is_holiday_eve`, `week_of_year`, `days_to_nearest_holiday`, `month`, `is_nettleie_night` (weekday 22–06 / weekend / holiday — also the tariff flag).

### Price history (known up to *T*)
Lags at 24, 48, 168, 336 h. Rolling mean/std/min/max over 24, 72, 168, 720 h. Yesterday's daily mean, yesterday's peak-hour index, yesterday's spread (max − min). Same-hour-last-week value. Neighbouring zones' recent prices and the NO1−NO2 spread (a congestion proxy).

**Regime B only:** the full cleared D+1 curve — its mean, its shape vector, and its spread. This is the feature that makes Regime B easy; verify it is absent from Regime A's feature set with a test, not with care.

### Weather forecast (as issued at or before *T*)
Per zone, aggregated over the zone's sample points:
- **Heating-degree-hours**: `max(0, 17 − temp_c)`, population-weighted — the demand driver.
- Population-weighted mean temperature; minimum temperature (cold-snap indicator).
- Mean and 90th-percentile wind speed at the coastal points — the supply driver, and the mechanism behind price collapses.
- Cumulative precipitation over the next 24/72/168 h at catchment points — hydro inflow expectation.
- Cloud fraction (solar in DE, which matters for NO2 via the interconnectors).
- **Forecast age**: hours between the weather run's `issued_at` and the target hour. Lets the model learn to trust fresh forecasts more.

### Hydrology (weekly, as-of dated)
Reservoir fill % per zone, deviation from the 20-year median, week-over-week change, snow water equivalent, and `weeks_since_publication`.

### Cross-market (Phase 3)
DE-LU and DK1 day-ahead prices, German wind + solar forecast, cross-border scheduled flows, EUR/NOK, TTF gas and EUA carbon (weekly is fine — these set the continental marginal cost that Norway's southern zones are coupled to).

### Feature hygiene
- One feature-builder function, shared by training and serving, taking `(as_of: datetime, horizon)` and returning a dataframe. **Serving must not have its own copy.** Training/serving skew is the classic silent killer; a single code path plus a test that asserts identical output for a fixed as-of is the fix.
- Every feature declares its availability lag in a manifest. The feature builder asserts against it.
- Features with >20 % missingness in the backtest window get dropped or explicitly justified.

---

## 5. Point-in-time correctness — the rule that makes this project credible

> **A feature used for a forecast issued at time *T* may only depend on information that existed at time *T*.**

Three ways this project would violate it if you weren't careful, all of which are easy to write and invisible in the results until you deploy:

1. **Weather observations instead of forecasts.** Training on what the temperature *was* gives a model that cannot exist in production, because at issue time you only have what MET *predicted*. This alone can look like a 30 % MAE improvement that evaporates on deployment.
2. **Revised prices.** ENTSO-E occasionally corrects published values. Training on the corrected series means training on data that didn't exist yet.
3. **Weekly hydrology back-filled.** Using Wednesday's reservoir number for the preceding Monday.

The defences:
- Every source table carries `observed_at_utc` / `issued_at_utc` (DATA.md).
- The feature builder takes `as_of` and every query is `WHERE issued_at_utc <= @as_of`.
- A dedicated test suite `PointInTimeTests` that, for a set of random historic `as_of` values, asserts no feature row references data with a later observation time.
- Weather features for the historic training period must come from **archived forecasts** where available. Where only Frost observations exist, tag those rows `weather_source = 'observed'` and either (a) exclude them from evaluation folds, or (b) report a separate, clearly-labelled "oracle weather" MAE as an upper bound on achievable skill. The gap between oracle-weather MAE and forecast-weather MAE is itself an interesting result — it tells you how much of your error is weather error rather than price-model error. Put that chart in the README.

---

## 6. Evaluation

### Metrics

| Metric | Why |
|---|---|
| **MAE** (øre/kWh), by zone × lead bucket | Primary. Interpretable in the user's units |
| **RMSE** | Secondary; sensitive to the price spikes that matter |
| **Pinball loss** averaged over the five quantiles | The real objective — measures the whole distribution |
| **Coverage** of the 50 % and 90 % intervals | Calibration. A 90 % band that contains 62 % of outcomes is a broken band |
| **Peak-hour hit rate** | Does the forecast identify the correct cheapest 4-hour window? Top-k overlap with truth |
| **Decision regret (kr)** ★ | See below |

★ **Decision regret is the metric that makes this project distinctive.** For each backtest day, run the optimizer twice — once on the forecast, once on the realised prices (perfect foresight) — and record the difference in the actual bill for a standard EV-charging task under a standard tariff. The result is in kroner. It answers the only question a user has: *does using this thing cost me less money than not using it?* A model with worse MAE can have lower regret, because errors in flat hours don't matter and errors that reorder the cheap window do. Report regret against the naive strategies "charge on arrival" and "charge at 02:00 always".

### Backtesting protocol

**Walk-forward, expanding window, issue-time anchored.**

```
for each fold f = 1..N:
    train_end   = fold_start(f)
    train       = all rows with target_time < train_end − embargo(24h)
    for each issue_time T in fold f (one per day at 06:00 and 13:15):
        features = build_features(as_of=T)      # PIT-correct by construction
        predict horizons 1..168
    evaluate against realised prices
```

- Folds: monthly, over at least 24 months → ~24 folds.
- **Embargo** of 24 h between train end and the first evaluated target to prevent lag features straddling the boundary.
- Retrain cadence in production: weekly. Test whether daily retraining helps; it usually doesn't, and "we tested it and it didn't, so we don't" is a good answer.
- Never use random k-fold. Never shuffle. A test asserts that no fold's training data postdates its evaluation data.

### Reporting

The backtest emits a JSON artefact and a markdown report per run, both committed to a results branch. The dashboard has a public `/model` page showing skill score by zone and lead time, calibration curves, and the regret comparison. **This page is the portfolio piece.** Recruiters who won't read your code will look at that page.

---

## 7. Training and serving

Python trains; .NET serves. That split is deliberate and defensible.

```
python/
  spotlys_model/
    features/        # the shared feature builder (source of truth)
    training/        # train.py, tune.py (Optuna), backtest.py
    export/          # onnx_export.py + parity check
    reports/         # markdown + plot generation
```

**Export:** LightGBM → ONNX via `onnxmltools`, opset pinned. A **parity test** is mandatory: run 1 000 random feature rows through both the Python model and the exported ONNX model and assert max absolute difference < 1e-5. This test runs in CI. Silent ONNX conversion drift on categorical or missing-value handling is a real failure mode.

**Serving:** `Microsoft.ML.OnnxRuntime` in-process in the .NET API. Inference for 5 zones × 168 hours × 5 quantiles is trivially fast — single-digit milliseconds — so no separate Python service, no gRPC hop, no extra container. This is a legitimate, explainable architectural win and the reason the whole thing runs comfortably on one small VPS.

**Model registry:**

```sql
CREATE TABLE model_version (
    id              bigserial PRIMARY KEY,
    zone            text NOT NULL,
    quantile        numeric(3,2) NOT NULL,
    trained_at_utc  timestamptz NOT NULL,
    train_data_to   timestamptz NOT NULL,
    git_sha         text NOT NULL,
    onnx_sha256     text NOT NULL,
    backtest_mae    numeric(10,4),
    backtest_skill  numeric(6,4),
    is_active       boolean NOT NULL DEFAULT false
);
```

Models are artefacts with provenance, not files someone copied to the server. Rollback = flip `is_active`.

**Every forecast is persisted as issued:**

```sql
CREATE TABLE price_forecast (
    zone             text NOT NULL,
    issued_at_utc    timestamptz NOT NULL,
    target_hour_utc  timestamptz NOT NULL,
    q05, q25, q50, q75, q95  numeric(10,4) NOT NULL,
    model_version_id bigint NOT NULL REFERENCES model_version(id),
    PRIMARY KEY (zone, issued_at_utc, target_hour_utc)
);
```

This table is what lets you compute live production accuracy — not backtest accuracy, *actual* accuracy on forecasts the system really made. A nightly job joins yesterday's forecasts to realised prices and writes the running production MAE. Showing live production accuracy next to backtest accuracy, and being able to explain any gap, is a senior-engineer move on a junior CV.

---

## 8. The optimizer

Separate from the forecast, and unit-testable independently.

**Problem.** Given: a forecast distribution per hour, a tariff model (DOMAIN.md §5), the month's current top-3 peaks, and a flexible load specification — schedule the load to minimise expected total cost.

**Load spec.**
```csharp
record FlexibleLoad(
    double EnergyKwh,          // e.g. 40 kWh EV charge
    double MaxPowerKw,         // charger limit
    double MinPowerKw,         // 0 for interruptible, >0 for e.g. a dryer
    DateTimeOffset NotBefore,
    DateTimeOffset Deadline,   // "ready by 07:00"
    bool Interruptible);
```

**Method.** The objective is separable across hours *except* for the capacity term, which couples the whole month through the top-3 peak. Solve as:

1. For a fixed peak ceiling `P`, the inner problem is a linear allocation over hours — sort by marginal cost after support and fill greedily up to `min(MaxPowerKw, P − baseload(h))`. O(n log n).
2. Outer loop over candidate ceilings `P` — only the step boundaries of the grid company's tariff table are interesting, so this is 5–8 candidates. Pick the ceiling with the lowest total.

This runs in microseconds, is exactly optimal for this structure, and — crucially — **is explainable to a user in one sentence**, which a black-box MILP is not. If the problem later grows (battery, solar export, multiple coupled loads), swap the inner solver for an LP via Google OR-Tools behind the same interface. Note this in an ADR.

**Under uncertainty.** Don't optimise the median. Optimise expected cost over the quantile fan, and report the spread:

> *Charging 01:00–05:00 Thursday: **38–52 kr**, versus 71–94 kr if you plug in at 17:00. Saving: **20–55 kr**.*

Two numbers, a range, and the counterfactual. Never a single fake-precise "you save 43.28 kr".

**Invariants worth property-testing** (CsCheck / FsCheck):
- Total scheduled energy always equals `EnergyKwh` (within float tolerance).
- Never schedules outside `[NotBefore, Deadline]`.
- Never exceeds `MaxPowerKw` in any hour.
- Cost of the optimised schedule ≤ cost of any naive schedule, on every generated input.
- Monotonicity: lowering a single hour's price never increases that hour's allocation.

That last set is where property-based testing earns its keep, and it makes a great README section.

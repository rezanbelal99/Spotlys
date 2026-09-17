# ROADMAP.md

Assumption: ~10 hours a week alongside your third year and the Telenor shifts. Twelve weeks to a deployed, demo-able, interview-ready system; phases 5 and 6 are optional depth if it holds your interest.

The ordering rule: **every phase ends with something deployed and visible.** No phase is "build the data layer". You should have a public URL to send someone by the end of week 3.

---

## Phase 0 — Spike (week 1)

Prove the two things that could sink the project before building anything around them.

- [ ] Pull 12 months of NO2 prices from hvakosterstrommen into a CSV. Confirm the format, the VAT exclusion, the `EXR` field.
- [ ] Pull a MET Locationforecast for one point with a correct `User-Agent`. Confirm cache header behaviour.
- [ ] In a Jupyter notebook: build B1 (seasonal naive) and a first LightGBM on calendar + price lags only. Measure MAE at 24 h and 48 h lead.
- [ ] Encode one real invoice by hand in a spreadsheet and reproduce the total.

**Done when:** you can state, in øre/kWh, what the seasonal-naive MAE is for NO2, and you have reproduced a real invoice to within a krone. If you cannot do the second one, the domain is not understood yet and everything downstream will be wrong.

**Kill criterion:** if reproducing the invoice takes more than two evenings, simplify the scope to a single grid company and single scheme for v1.

---

## Phase 1 — Skeleton and ingestion (weeks 2–3)

- [ ] Solution scaffolding, `Directory.Build.props`, `.editorconfig`, warnings-as-errors from commit one
- [ ] Postgres schema + EF migrations; `price_observation`, `ingestion_run`
- [ ] `IngestDayAheadPrices` job with backfill (rate-limited) and daily poll
- [ ] Contract tests against recorded hvakosterstrommen responses
- [ ] `GET /api/v1/prices/{zone}`; `/api/v1/status`
- [ ] Minimal React app rendering the ribbon for today and tomorrow, real colours, no animation yet
- [ ] `docker compose up` works from a clean clone; `ci.yml` green
- [ ] **Deployed to the droplet with a real domain**

**Done when:** a stranger can open the URL and see today's prices for their zone, and the page tells them honestly if the data is stale.

---

## Phase 2 — The tariff engine (weeks 4–5)

This is the phase that makes the project different from everyone else's. Do not skip ahead to the model.

- [ ] `SupportScheme` — strømstøtte (hourly, 90 % above threshold) and Norgespris (flat, capped, cabin variant)
- [ ] `GridTariff` — day/night energiledd, capacity steps, Norwegian holiday calendar
- [ ] Levies and VAT with NO4 handling; `scheme_parameter` and `grid_tariff` tables seeded for 3–4 grid companies
- [ ] Golden-file tests against 2–3 real invoices
- [ ] Property tests for monotonicity and boundary behaviour at step edges
- [ ] DST fixtures for both transition dates
- [ ] `POST /api/v1/bill/simulate`; CSV consumption import
- [ ] UI: the nettleie band under the ribbon; per-hour detail readout shows *cost after support*, never raw spot
- [ ] Demo mode with a seeded fictional household

**Done when:** you can upload a year of your own consumption CSV and the app reproduces your actual bills.

---

## Phase 3 — The forecast (weeks 6–8)

- [ ] Weather ingestion with the three-timestamp schema; hydrology weekly job
- [ ] Shared feature builder in Python with an availability manifest
- [ ] Baselines B0–B4 implemented and measured
- [ ] LightGBM quantile models per zone, Regime A and Regime B
- [ ] Walk-forward backtest harness, 24 monthly folds, embargo, issue-time anchored
- [ ] Point-in-time test suite
- [ ] ONNX export + parity test; `model_version` registry; `price_forecast` persistence
- [ ] `ScoreProductionForecasts` nightly job
- [ ] Public `/model` page with skill score, calibration and the oracle-weather gap
- [ ] UI: forecast fan on the ribbon; the confirmed/varslet distinction; the 13:00 cross-fade

**Done when:** the `/model` page is live, and you can say "my model beats seasonal naive by X % at 48-hour lead in NO2, and here's where it doesn't."

---

## Phase 4 — Advice (weeks 9–10)

- [ ] Optimizer with the peak-ceiling outer loop; property tests
- [ ] `POST /api/v1/plan`; plan view with the snapping bracket
- [ ] Peak tracking: month-to-date top-3 døgnmaks per meter
- [ ] Monthly cap projection for Norgespris users — "you'll cross 5 000 kWh around the 24th"
- [ ] Regime advisor: `POST /api/v1/advisor/regime`, with the decomposed comparison and uncertainty hatching
- [ ] Decision-regret metric computed over the backtest and shown on `/model`
- [ ] Accounts, GDPR export/delete, privacy policy
- [ ] PWA install + web push plan notifications

**Done when:** the app tells a specific household, with a range and a counterfactual, what to do and whether it matters for them.

---

## Phase 5 — Polish and proof (weeks 11–12)

- [ ] The tide entrance and the full motion spec; reduced-motion pass
- [ ] Full a11y sweep; axe in CI; deuteranopia screenshot committed
- [ ] Norwegian + English locales complete
- [ ] Grafana dashboards; drift alert; monthly restore test
- [ ] README with architecture diagram, screenshots, live link, "what I'd do differently"
- [ ] ADRs written up; `DECISIONS-I-GOT-WRONG.md` populated
- [ ] A 3-minute screen recording walking through the system — this is what you attach to applications

**Done when:** you would be comfortable putting the repo URL on a CV sent to Handelsbanken.

---

## Phase 6 — Optional depth (pick at most one)

| Option | Adds | Cost |
|---|---|---|
| **HAN-port live reading** (ESP32 + MQTT) | Real-time peak guard, best demo in the project | 2–3 weeks, hardware |
| **ENTSO-E + continental features** | Cross-border coupling, real modelling depth, likely accuracy gain in NO2 | 2 weeks |
| **Battery / solar scheduling** | Genuine optimisation problem; justifies the LP swap | 2 weeks |
| **Pooled model with zone embeddings** | A real modelling experiment with a publishable result | 1 week |

Do not do more than one. A finished project with one deep thing beats a sprawling one with four half-built things — which is also the reason this list exists at the bottom of the document rather than the top.

---

## Checkpoints where you should be willing to stop

- **End of Phase 2** is already a legitimate portfolio project: a correct Norwegian bill simulator with real invoice validation. If the semester goes sideways, ship that and write the README.
- **End of Phase 3** is the strongest version per hour invested, and it's the one that answers "show me something hard you built."
- Phases 4–6 are upside.

---

## Where this connects to your existing work

- The Gitea self-hosted runner, Docker Compose and Watchtower work from IKT206 is directly reusable — `container.yml` is a variation on what you already built.
- The ONNX-in-.NET pattern is the same one that makes your machine-vision course work resume-legible, so building it here means you only have to learn it once.
- The point-in-time / backtesting discipline is the same reasoning as the Bayesian and regression work in MA-223, applied to a system rather than a report. That's a connection worth making explicitly in an interview.

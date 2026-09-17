# PROMPTS.md — driving Claude Code on this project

## How to use this

Put the docs in the repo first (`docs/`, `CLAUDE.md` at the root), commit them, *then* start
Claude Code. The documents are the specification; the prompts below are just pointers into
them. A prompt that restates the spec instead of referencing it will drift from it.

Standing practice, every session:

- **Start in plan mode** (Shift+Tab). Read → propose → approve → build. This is the single
  biggest reliability difference and it's free.
- **One vertical slice per session.** End with something that runs.
- `/clear` between slices. Long contexts make Claude forget the rules at the top of CLAUDE.md.
- **Challenge the output** before merging: *"grill me on these changes — prove the tariff
  engine handles the October DST day and the NO4 VAT case, and don't open a PR until it does."*
- After a mediocre implementation, don't patch it: *"knowing what you know now, scrap this
  and implement the version you'd write from scratch."*
- Update `## Current phase` and the `## Things that have bitten this project` section in
  CLAUDE.md as you go. That file is the project's memory.

---

## 0. Session zero — bootstrap

```
Read CLAUDE.md and every document in docs/ before doing anything. Then, in plan mode,
propose a Phase 0 spike only — do not scaffold the full solution.

Phase 0 goals, from docs/ROADMAP.md:
  1. A throwaway script that pulls 12 months of NO2 day-ahead prices from
     hvakosterstrommen.no into a CSV. Confirm the VAT convention and the EXR field.
  2. A throwaway script that fetches a MET Locationforecast for one point with a compliant
     User-Agent, honouring cache headers.
  3. A notebook implementing baseline B1 (seasonal naive) and a first LightGBM on calendar
     and price-lag features only, reporting MAE at 24h and 48h lead for NO2.
  4. A spreadsheet-equivalent script that reproduces one real Norwegian electricity invoice
     from a consumption profile, using the rules in docs/DOMAIN.md.

Constraints:
  - Everything under spike/. None of it ships. Say so in a README in that folder.
  - Rate-limit the backfill to 2 requests/second and cache responses to disk so we never
    re-fetch.
  - Before writing any code, tell me what you believe the VAT convention is for each source
    and where in the docs you got it.

I'll give you a real invoice to test against — ask me for it when you need it.
```

---

## 1. Phase 1 — skeleton and ingestion

```
Phase 1 from docs/ROADMAP.md. Plan first.

Build the walking skeleton: hvakosterstrommen ingestion → Postgres → API → a React ribbon
showing today and tomorrow for one zone, deployed.

Follow docs/ARCHITECTURE.md §2 for the solution layout and §4 for the schema, and
docs/DATA.md §6 for the ingestion contract — idempotent, bounded, observed, honest on
failure. Every ingested row carries observed_at_utc.

Set up the engineering baseline in the first commit, not later:
  - Directory.Build.props with nullable, warnings-as-errors, AnalysisMode=All,
    InvariantGlobalization=false
  - .editorconfig, central package management, Conventional Commits hook
  - docker compose for db + api + worker + web, and a make bootstrap target that gets a
    clean clone running with seeded fixtures in under five minutes
  - .gitea/workflows/ci.yml per docs/DEVOPS.md §5
  - .claude/settings.json hooks per docs/DEVOPS.md §2, including the dangerous-command block
  - gitleaks in pre-commit

Order of work: compose stack and CI green on an empty solution first, then schema, then the
ingestion job with contract tests against recorded fixtures (no network in CI), then the
endpoint, then the UI.

Stop and ask me before: choosing the migration strategy, adding any NuGet or npm package
not named in docs/ARCHITECTURE.md §7.

Done when a stranger can open the deployed URL, see today's NO2 prices, and be told plainly
if the data is stale.
```

---

## 2. Phase 2 — the tariff engine

```
Phase 2 from docs/ROADMAP.md. This is the most correctness-sensitive work in the project.
Re-read docs/DOMAIN.md in full before planning. Plan first, and in your plan tell me how
you'll model the strømstøtte / Norgespris switch and the kapasitetsledd top-3 peak rule.

Build Spotlys.Domain.Pricing:
  - SupportScheme: strømstøtte (hourly, 90% above the threshold) and Norgespris (flat,
    monthly cap, cabin variant, above-cap fallback with no support)
  - GridTariff: day/night energiledd with the Norwegian holiday calendar, capacity steps
    from the average of the three highest hourly peaks on three different days
  - Levies and VAT, with NO4 having no VAT
  - scheme_parameter and grid_tariff tables, seeded for three grid companies with source URLs

Hard requirements:
  - Zero magic numbers. Every rate comes from the versioned table.
  - decimal throughout. Unit and VAT status in every name.
  - TimeProvider injected; no DateTime.Now anywhere.
  - Fixture tests for both DST transition days and for a negative-price hour.
  - Property tests (CsCheck) for monotonicity and for behaviour exactly at capacity-step
    boundaries.
  - Golden-file tests (Verify) reproducing real invoices I'll give you, to within 1 øre.

Spotlys.Domain must reference nothing. Add the NetArchTest that enforces it and wire it into
CI in this same change.

When the engine is done, wire POST /api/v1/bill/simulate and the CSV consumption import, and
make the UI's hour readout show cost after support — never raw spot. Demo mode with a seeded
fictional household ships in this phase, not later.
```

---

## 3. Phase 3 — the forecast

```
Phase 3 from docs/ROADMAP.md. Read docs/FORECASTING.md fully, especially §5 on point-in-time
correctness. Plan first.

Build in this order and do not skip ahead:
  1. Weather ingestion with the three-timestamp schema (issued_at, fetched_at, valid_at) and
     the hydrology weekly job. Compliant MET User-Agent, cache headers honoured.
  2. The shared feature builder in python/, taking as_of, with an availability manifest per
     feature. One implementation, used by both training and evaluation.
  3. Baselines B0–B4 from §2, measured, before any model.
  4. The walk-forward backtest harness: monthly folds, 24h embargo, issue-time anchored,
     metrics split by zone and lead bucket.
  5. Only then LightGBM quantile models, Regime A and Regime B.
  6. ONNX export with the mandatory parity test, model_version registry, price_forecast
     persistence, and the nightly ScoreProductionForecasts job.
  7. The public /model page and .gitea/workflows/model.yml with the skill-score gate.

Non-negotiable: a PointInTime test suite that, for random historic as_of values, asserts no
feature reads data observed later. Write those tests before the model, not after.

Report the baselines to me before you train anything. If LightGBM doesn't beat B4 by a clear
margin, tell me that plainly rather than tuning until it does.
```

---

## 4. Phase 4 — advice

```
Phase 4 from docs/ROADMAP.md. Plan first.

  - The optimizer per docs/FORECASTING.md §8: greedy inner allocation under a fixed peak
    ceiling, outer loop over the grid company's step boundaries. Property tests for all five
    invariants listed there. Keep it in Spotlys.Domain.Scheduling with no I/O.
  - POST /api/v1/plan and the plan view with the snapping bracket from docs/DESIGN.md §5.
  - Month-to-date top-3 peak tracking per meter, and the Norgespris monthly-cap projection
    ("you'll cross 5 000 kWh around the 24th").
  - The regime advisor: POST /api/v1/advisor/regime, with the decomposed comparison and
    forecast uncertainty drawn as hatching, not as a solid segment.
  - Decision regret computed over the backtest and published on /model.

Personal data enters the system in this phase, so in the same PR: accounts, GDPR export and
delete endpoints, the retention job, and docs/DATA-PROTECTION.md. Not afterwards.

Every user-facing number is a range plus a counterfactual. If you find yourself formatting a
single kroner figure as a headline, stop and tell me why.
```

---

## 5. Phase 5 — polish, accessibility, operations

```
Phase 5 from docs/ROADMAP.md. Plan first.

  - Implement the motion spec in docs/DESIGN.md §5 exactly: the 760ms tide entrance once per
    session, user-triggered motion everywhere else, no scroll-triggered reveals anywhere.
    prefers-reduced-motion handled once at the MotionConfig level.
  - Full accessibility pass per §6: composite-widget keyboard model for the ribbon, visually
    hidden table rendition, contrast verified per ramp stop, axe-core in the Playwright run
    wired into CI as a blocking gate.
  - Norwegian and English locales complete, ICU plurals, no concatenation.
  - Operations per docs/DEVOPS.md: Grafana dashboards and Prometheus alert rules committed
    as code, one runbook per alert in docs/runbooks/, the monthly restore-test workflow, and
    the disaster-recovery procedure — then actually rehearse it once and record how long it
    took.
  - README with architecture diagram, screenshots, live link, the cost figure, an
    "Operational trade-offs" section, and "what I'd do differently".
  - Write the outstanding ADRs listed in docs/ENGINEERING.md §5, and populate
    docs/DECISIONS-I-GOT-WRONG.md from our actual history in this repo — find the reversals
    in the git log, don't invent them.
```

---

## 6. Reusable slash commands

Put these in `.claude/commands/` so they're one keystroke instead of a paragraph.

**`/domain-check`** — run before merging anything that touches pricing:
```
Review the staged changes against docs/DOMAIN.md. Check specifically: raw spot price
reaching the UI; hard-coded scheme parameters; float or double used for money; missing VAT
handling for NO4; DST and holiday edge cases; whether any claimed saving overstates the
marginal benefit after strømstøtte. List findings by severity. Do not fix anything yet.
```

**`/pit-check`** — run before merging anything that touches features or training:
```
Trace every feature in the changed code back to its source table and prove the query is
bounded by as_of. Flag anything reading a weather observation rather than a forecast, and
anything that could see a revised price. Report as a table: feature, source, as_of bound,
verdict.
```

**`/ship-check`** — run before every push:
```
Run make check. Then verify the definition-of-done list from CLAUDE.md against the staged
changes and report each item as pass, fail or not-applicable with one line of evidence.
Don't claim a pass you haven't verified.
```

**`/adr`** —
```
Write an ADR in docs/adr/ for the decision we just made, in the Nygard format used by
docs/adr/0002. Include the options we rejected and the condition under which we'd revisit.
Number it sequentially.
```

## 7. Subagents worth defining

In `.claude/agents/`:

- **`domain-reviewer`** — reads only `docs/DOMAIN.md` and the diff, in clean context.
  Catches tariff errors the main session has become blind to after a long day of writing it.
- **`test-writer`** — given a domain type, writes property tests against the invariants in
  the docs rather than tests that mirror the implementation.
- **`runbook-writer`** — given an alert rule, drafts the four-section runbook.

The value of a subagent here is the clean context: a reviewer that hasn't spent an hour
rationalising your implementation catches things the author can't.

---

## 8. What not to ask Claude Code to do

- **Don't ask it to decide Norwegian regulation.** Thresholds, caps, tariff structures and
  VAT rules come from you, with a source URL, into `scheme_parameter`. You work in this
  market; it's guessing from a snapshot.
- **Don't ask for "the whole app"** in one prompt. You'll get a plausible skeleton, no
  working ingestion, and a tariff engine that's subtly wrong in ways you won't find for
  weeks.
- **Don't let it tune the model until the metric looks good.** The baselines and the honest
  comparison are the portfolio value. A flattering number you can't defend is worse than a
  modest one you can.
- **Don't let it skip the point-in-time work** because the model scores better without it.
  That's the exact failure the project is designed to demonstrate you understand.

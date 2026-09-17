# ARCHITECTURE.md

## 1. Shape of the system

```
                    ┌──────────────────────────────────────────┐
  hvakosterstrommen │                                          │
  MET Locationfc.   │   Spotlys.Ingestion  (Worker Service)    │
  NVE hydrology     │   Quartz schedules · Polly · idempotent  │
  ENTSO-E           │                                          │
                    └────────────────────┬─────────────────────┘
                                         │ writes
                                 ┌───────▼────────┐
                                 │  PostgreSQL 17 │
                                 │  partitioned   │
                                 │  time series   │
                                 └───┬────────┬───┘
                    reads            │        │           reads
        ┌────────────────────────────┘        └────────────────────┐
        │                                                          │
┌───────▼─────────────────────────┐              ┌─────────────────▼──────────┐
│  Spotlys.Api  (ASP.NET Core)    │              │  python/  (offline)        │
│  · Tariff engine                │   ONNX  ◄────┤  LightGBM training         │
│  · ONNX Runtime inference       │  artefact    │  Optuna tuning             │
│  · Optimizer                    │              │  Walk-forward backtest     │
│  · Output caching               │              │  Report generation         │
└───────┬─────────────────────────┘              └────────────────────────────┘
        │ JSON
┌───────▼─────────────────────────┐
│  Spotlys.Web  (React 19 + Vite) │
└─────────────────────────────────┘
```

Three deployable units plus a database. Not microservices — there is no scaling or team-boundary reason for more, and inventing service boundaries you don't need is a negative signal, not a positive one. Say that out loud in the ADR.

---

## 2. Solution layout

```
Spotlys.sln
├── src/
│   ├── Spotlys.Domain/            # no dependencies. Pure logic
│   │   ├── Pricing/               #   TariffEngine, SupportScheme, GridTariff
│   │   ├── Scheduling/            #   Optimizer, FlexibleLoad, Schedule
│   │   ├── Forecasting/           #   ForecastFan, Quantile, LeadTime
│   │   └── Metering/              #   ConsumptionSeries, PeakTracker
│   ├── Spotlys.Application/       # use cases, ports (interfaces), DTOs
│   ├── Spotlys.Infrastructure/    # EF Core, HTTP clients, ONNX adapter
│   ├── Spotlys.Api/               # Minimal API endpoints, auth, OpenAPI
│   ├── Spotlys.Ingestion/         # Worker Service, Quartz jobs
│   └── Spotlys.Web/               # React app
├── tests/
│   ├── Spotlys.Domain.Tests/          # fast, pure, thousands of cases
│   ├── Spotlys.Application.Tests/     # use cases with test doubles
│   ├── Spotlys.Integration.Tests/     # Testcontainers Postgres
│   ├── Spotlys.PointInTime.Tests/     # leakage guards
│   └── Spotlys.E2E/                   # Playwright
├── python/
├── deploy/                        # compose, k3s manifests, Grafana dashboards
└── docs/
```

**The dependency rule:** `Domain` references nothing. `Application` references `Domain`. `Infrastructure` and `Api` reference `Application`. Enforced by an architecture test (NetArchTest) that fails the build on violation, not by discipline.

**Why `Domain` matters here specifically:** the tariff engine and the optimizer are the two pieces most likely to be wrong, most valuable to test exhaustively, and most interesting to a reviewer. Keeping them free of EF Core, HTTP and clocks means they can be tested with thousands of generated cases in milliseconds. Inject `TimeProvider` (built into .NET 8+) rather than calling `DateTime.Now` — this is also what makes the "what happens at 13:00 on the day the clocks change" tests possible.

---

## 3. Time handling

Three rules, stated here because getting them wrong costs a week.

1. **Store UTC, always.** `timestamptz` in Postgres, `DateTimeOffset` in C#.
2. **Compute tariffs in Europe/Oslo.** Night rate boundaries (22:00–06:00), the daily peak window and the calendar day are all local concepts. Use `TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo")` and the IANA database — and add `Microsoft.ICU` / set `InvariantGlobalization=false` in the container or the ID won't resolve on Alpine.
3. **DST days have 23 and 25 hours.** The March day has no 02:00; the October day has two. Both exist in the price feed. A test fixture for each transition date is mandatory and will catch a bug in your chart, your aggregation and your optimizer. Most consumer energy apps in Norway visibly break on these two days each year; yours won't, and you'll be able to say why.

---

## 4. Database

PostgreSQL 17. Plain Postgres, not TimescaleDB: the volume is small (five zones × hourly × a few years ≈ 200 k rows for prices, a few million for weather) and adding an extension for that is unjustified. **Native declarative partitioning by month** on the large tables plus BRIN indexes on time columns gives you what you need. Record the decision, and the trigger point at which you'd revisit it (>50 M rows or sub-second range-scan requirements), in an ADR.

Core tables — the time series ones are in DATA.md and FORECASTING.md. Application-side:

```sql
CREATE TABLE app_user (
    id uuid PRIMARY KEY,
    email citext UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE meter_profile (
    id              uuid PRIMARY KEY,
    user_id         uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
    zone            text NOT NULL,
    grid_company_id text NOT NULL REFERENCES grid_company(id),
    support_scheme  text NOT NULL,          -- 'stromstotte' | 'norgespris'
    is_cabin        boolean NOT NULL DEFAULT false,
    supplier_markup_ore numeric(8,4) NOT NULL DEFAULT 0,
    supplier_monthly_fee_nok numeric(8,2) NOT NULL DEFAULT 0
);

-- Scheme parameters are data, not code. See DOMAIN.md §7.
CREATE TABLE scheme_parameter (
    scheme        text NOT NULL,
    parameter     text NOT NULL,        -- 'threshold_ex_vat','support_rate','cap_kwh_month','rate_ex_vat'
    valid_from    date NOT NULL,
    valid_to      date,
    value         numeric(12,4) NOT NULL,
    source_url    text NOT NULL,
    PRIMARY KEY (scheme, parameter, valid_from)
);

CREATE TABLE grid_tariff (
    grid_company_id text NOT NULL,
    valid_from      date NOT NULL,
    energy_day_ore  numeric(8,4) NOT NULL,
    energy_night_ore numeric(8,4) NOT NULL,
    capacity_steps  jsonb NOT NULL,       -- [{from_kw, to_kw, monthly_nok}, ...]
    PRIMARY KEY (grid_company_id, valid_from)
);
```

`scheme_parameter` with `source_url` is a small thing that reads extremely well in review: it says you understand that this data is a political artefact with provenance, not a magic number.

Migrations: EF Core migrations, applied by a dedicated init container, never by the API at startup. Every migration reviewed for lock behaviour.

---

## 5. Scheduled jobs

| Job | Schedule (Europe/Oslo) | Notes |
|---|---|---|
| `IngestDayAheadPrices` | 12:45, then every 5 min until success, max 14:30 | Alert if unresolved |
| `IngestDayAheadPricesBackfill` | manual | One-shot, rate-limited |
| `IngestWeatherForecast` | 04:00, 10:00, 16:00, 22:00 | Honours MET cache headers |
| `IngestHydrology` | Wednesdays 09:00 | Weekly NVE publication |
| `RunForecast (Regime A)` | 06:00 | Persists to `price_forecast` |
| `RunForecast (Regime B)` | 13:15 | After auction publication |
| `ScoreProductionForecasts` | 02:00 | Joins yesterday's forecasts to truth |
| `RetrainModels` | Sundays 03:00 | Trains, backtests, promotes only if skill improves |
| `SendPlanNotifications` | 20:00 | Web push, user opt-in |
| `RecomputeMonthlyPeaks` | hourly | Maintains top-3 døgnmaks per meter |

Quartz.NET with the Postgres job store so schedules survive restarts and don't double-fire across replicas. Every job wrapped in the same decorator that writes `ingestion_run` and emits telemetry.

**Model promotion is automatic but gated:** a retrained model becomes `is_active` only if its walk-forward skill score on the most recent 3 folds is ≥ the incumbent's minus a small tolerance. Otherwise it is stored, flagged, and a notification is raised. Automatic promotion without a gate is how models silently rot.

---

## 6. API surface

Minimal APIs, OpenAPI generated, versioned under `/api/v1`.

```
GET  /api/v1/prices/{zone}?from&to                 → realised prices
GET  /api/v1/forecast/{zone}?issued_at&horizon     → quantile fan
GET  /api/v1/model/skill                           → public accuracy page data
POST /api/v1/plan                                  → optimizer: load spec → schedule
POST /api/v1/bill/simulate                         → tariff engine: profile + prices → bill
POST /api/v1/advisor/regime                        → Norgespris vs spot comparison
GET  /api/v1/meters/{id}/peaks                     → month-to-date top-3
POST /api/v1/meters/{id}/consumption:import        → CSV upload
GET  /api/v1/status                                → freshness of every feed
```

- **Output caching** on the public read endpoints keyed by zone + hour, invalidated by the ingestion jobs. Forecast responses are identical for all anonymous users; there is no reason to hit the database twice.
- **Rate limiting** via the built-in middleware, stricter on `/plan` and `/bill/simulate`.
- **Problem Details** (RFC 9457) for every error. No bare 500s with stack traces.
- **Demo mode**: `/api/v1/demo/*` serves a seeded fictional household so the deployed app is fully explorable without signing up. Recruiters will not create an account. Build this in Phase 2, not as an afterthought.
- Auth (Phase 4 only, when personal data enters): ASP.NET Core Identity with cookie auth, or BankID-style OIDC if you want the Norwegian flavour. Do not build auth before there is something to protect.

---

## 7. Frontend architecture

- **React 19 + TypeScript strict + Vite.** TanStack Query for server state; no global state library — there is very little client state, and adding Redux to this would be a tell.
- **Charts are hand-written SVG using D3 scales only** (`d3-scale`, `d3-shape`, `d3-array`), not a charting library. Reasons: the core visual is a bespoke price ribbon with a quantile fan and a scrub interaction (DESIGN.md), which no library renders well; React owns the DOM and D3 only computes geometry, which is the clean pattern; and it demonstrates you can build a visualisation rather than configure one. Bundle stays small.
- **Motion** (`motion/react`) for orchestrated animation, with `prefers-reduced-motion` handled once at the provider level.
- Routes: `/` (today + tomorrow), `/plan` (week + optimizer), `/bill` (regime advisor), `/model` (public accuracy), `/settings`.
- PWA: installable, offline shell, web push for plan notifications. A Norwegian energy app that lives on the home screen is materially more useful than one that doesn't.

---

## 8. Deployment

**Local:** `docker compose up` brings up Postgres, the API, the ingestion worker, the frontend dev server, plus Grafana/Prometheus/Loki. Seeded with a fixture dataset so a fresh clone is useful in under five minutes. A `make bootstrap` target. If a new contributor — or you in four months — can't run it in one command, the project is already decaying.

**Production:** single-node k3s on a DigitalOcean droplet (you already run this stack). Manifests in `deploy/k3s`. Gitea Actions on your self-hosted runner builds, tests, scans and pushes images; deployment by manifest apply, with Watchtower as the simpler fallback path documented for anyone who wants Compose instead.

**Cost target:** under 150 kr/month all-in. State that in the README — cost consciousness is a signal too.

**Backups:** nightly `pg_dump` to object storage, with a **restore test** in CI monthly that spins up a container from the latest dump and asserts row counts. An untested backup is not a backup.

---

## 9. Security and privacy posture

- No secrets in the repo; `dotnet user-secrets` locally, Kubernetes secrets in production, and a `.env.example` that documents every variable.
- CSP with no `unsafe-inline`; the SVG charts don't need it.
- Consumption CSV upload: size cap, content-type check, parsed with a strict CSV reader into a validated DTO, never `File.ReadAllText` into a string builder. Treat uploaded files as hostile.
- GDPR: data export and delete endpoints, a retention job, a written privacy policy, and a `docs/DATA-PROTECTION.md` covering lawful basis and retention. For a project handling household consumption data this is not optional and it is a genuine differentiator on a Norwegian CV.

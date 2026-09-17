# ENGINEERING.md — code quality, testing, CI/CD, operations

The point of this document: a reviewer who opens the repo should be able to tell within two minutes that the project was engineered rather than assembled.

---

## 1. Non-negotiable repo settings

`Directory.Build.props` at the root, applying to every project:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors></WarningsNotAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisMode>All</AnalysisMode>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

Plus:
- **`.editorconfig`** with explicit rules, committed. `dotnet format --verify-no-changes` runs in CI.
- **Central package management** (`Directory.Packages.props`) so versions are declared once.
- **TypeScript `strict: true`**, plus `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`. ESLint with `@typescript-eslint/strict-type-checked`. Prettier. `tsc --noEmit` in CI.
- **Renovate** for dependency updates, grouped weekly, auto-merge on patch when tests pass.
- **Conventional Commits**, enforced by a commit-msg hook, feeding an auto-generated CHANGELOG.

Warnings-as-errors is the one that does the most work. It is also the one most people turn off after a week — don't.

---

## 2. Testing strategy

Not "aim for 80 % coverage". Coverage is a diagnostic, not a goal. Different layers get different kinds of test, and each kind has a job.

| Layer | Kind | Tooling | What it buys |
|---|---|---|---|
| `Domain.Pricing` | Example + **golden-file** tests | xUnit, Verify | A real invoice, digitised, asserted to the øre |
| `Domain.Scheduling` | **Property-based** | CsCheck | The optimizer invariants in FORECASTING.md §8 |
| `Domain` (time) | Fixture tests on DST dates, holidays, NO4 VAT | xUnit + `TimeProvider` | The bugs that bite once a year |
| `Application` | Use-case tests with fakes | xUnit, NSubstitute | Orchestration correctness |
| `Infrastructure` | **Integration** | Testcontainers Postgres | Real SQL, real migrations, real timezone behaviour |
| Ingestion clients | **Contract** tests against recorded responses | WireMock.Net + committed fixtures | Upstream shape changes detected, no network in CI |
| Feature builder | **Point-in-time** tests | xUnit + generated `as_of` values | No leakage. The credibility test |
| Model | Parity + regression | pytest + ONNX parity, backtest gate | The model that ships is the model you measured |
| API | Endpoint tests | `WebApplicationFactory` | Contracts, status codes, Problem Details |
| Frontend | Unit + component | Vitest, Testing Library | Logic and rendering |
| Whole system | E2E | Playwright | Three critical journeys only |
| A11y | Automated | axe-core inside Playwright | Contrast, roles, focus order |

### The golden-file invoice test — do this early, it's the best test in the project

Take a real Norwegian electricity invoice (yours, or a coworker's with permission and the identifiers stripped). Encode its consumption profile, grid company, zone, month and scheme as a fixture. Assert that `TariffEngine` reproduces every line on that invoice within 1 øre.

```csharp
[Theory]
[MemberData(nameof(RealInvoices))]
public async Task TariffEngine_reproduces_real_invoice(InvoiceFixture fixture)
{
    var bill = _engine.Calculate(fixture.Profile, fixture.Consumption, fixture.Prices);
    await Verify(bill).UseParameters(fixture.Name);
}
```

This single test is worth more than a hundred unit tests, because it validates the *whole* domain model against ground truth. It is also a fantastic thing to describe in an interview: "I validated my tariff engine against real invoices from three different grid companies and found two bugs in my capacity-step boundary handling."

### What is deliberately not tested

Write this down in the repo. Knowing what not to test is a senior signal:
- Getters, mappers, and EF configuration — covered transitively by integration tests.
- Exact chart pixel output — covered by visual review, not assertions.
- Third-party library behaviour.

### CI gates

The build fails on: any compiler warning; `dotnet format` diff; any failing test; coverage of `Spotlys.Domain` below 90 % (only that project — a global threshold rewards testing trivia); any axe-core violation; any high/critical Trivy finding in the image; a model backtest skill score regression beyond tolerance.

---

## 3. CI/CD

Gitea Actions on the self-hosted runner. Five workflows.

```
.gitea/workflows/
  ci.yml            # every push: build, format, test, coverage, arch tests
  container.yml     # main: build multi-arch images, Trivy scan, push, deploy
  model.yml         # weekly + manual: retrain, backtest, parity, gate, publish report
  freshness.yml     # every 30 min: probe /api/v1/status, alert on stale feeds
  restore-test.yml  # monthly: restore last night's dump into a throwaway container
```

`ci.yml` sketch:

```yaml
jobs:
  build-test:
    runs-on: self-hosted
    services:
      postgres: { image: postgres:17, env: { POSTGRES_PASSWORD: test } }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet format --verify-no-changes
      - run: dotnet build -warnaserror
      - run: dotnet test --collect:"XPlat Code Coverage"
      - run: dotnet test tests/Spotlys.PointInTime.Tests
      - uses: codecov/codecov-action@v4        # or a self-hosted report artefact
  frontend:
    steps:
      - run: npm ci && npm run lint && npx tsc --noEmit && npm run test && npm run build
```

`model.yml` is the interesting one and the thing nobody else's student project has:

```yaml
- run: python -m spotlys_model.training.train --zones NO1..NO5
- run: python -m spotlys_model.training.backtest --folds 24 --report out/report.md
- run: python -m spotlys_model.export.onnx_export && pytest tests/test_onnx_parity.py
- run: python -m spotlys_model.training.gate --incumbent-skill $INCUMBENT --tolerance 0.01
- uses: actions/upload-artifact@v4
  with: { name: backtest-report, path: out/ }
```

The gate step exits non-zero if the new model is worse, and the report is published to the public `/model` page. **A CI pipeline that can fail because a machine learning model got worse is a genuinely uncommon thing to have built**, and it's an easy thing to talk about for five minutes.

---

## 4. Observability

- **OpenTelemetry** for traces, metrics and logs, exported to Prometheus + Grafana + Loki in `deploy/`. Instrument ingestion jobs, the ONNX inference path and the optimizer.
- **Serilog** structured logging with a request-correlation enricher. No `Console.WriteLine`, no string-interpolated log messages — message templates only, so logs are queryable.
- **Health checks** at `/health/live` and `/health/ready`, with the readiness check including database connectivity and data freshness.
- **The metrics that matter** (dashboard committed as JSON in `deploy/grafana/`):
  - `spotlys_feed_lag_seconds{feed}` — how stale is each source
  - `spotlys_forecast_mae_ore{zone,lead_bucket}` — live production accuracy, updated nightly
  - `spotlys_inference_duration_ms`
  - `spotlys_optimizer_duration_ms`
  - `spotlys_http_server_duration_seconds` p95 by route
- **Alerts:** day-ahead prices not ingested by 14:30; any feed lag beyond its declared `MaxStaleness`; production MAE drifting more than 25 % above backtest MAE over a 7-day window (concept drift — the model is stale or the market has changed).

That last alert is the one to be proud of. It closes the loop between "I trained a model" and "I operate a model."

---

## 5. Documentation practice

- **ADRs** in `docs/adr/NNNN-title.md`, one page each, in the Nygard format (Context / Decision / Consequences). Write them at the moment of the decision, not retrospectively. Candidates already identified:
  - 0001 Postgres partitioning instead of TimescaleDB
  - 0002 Python trains, .NET serves via ONNX
  - 0003 Per-zone models instead of one pooled model
  - 0004 Hand-written SVG charts instead of a charting library
  - 0005 Elhub third-party access is not obtainable; CSV + HAN instead
  - 0006 Greedy peak-ceiling optimizer instead of a MILP
  - 0007 Scheme parameters as versioned data, not constants
  - 0008 Three deployables, not microservices
- **README** with an architecture diagram, a five-minute local setup, a screenshot, a link to the live demo, and a short "what I'd do differently" section. That last section is disproportionately persuasive.
- **`docs/DECISIONS-I-GOT-WRONG.md`** — a running log of things you built and then reversed, with the reasoning. Genuinely rare, genuinely impressive, and it costs nothing but honesty.
- Every public type in `Spotlys.Domain` has an XML doc comment explaining *why*, not restating the signature.

---

## 6. Definition of done

A change is done when all of these are true. Put this in `CONTRIBUTING.md` and in the PR template, and hold yourself to it even as a solo developer — the discipline is the portfolio artefact.

- [ ] Builds clean with warnings as errors
- [ ] Tests at the right layer; new domain logic has property or golden-file tests
- [ ] No feature reads data that would not have existed at its `as_of`
- [ ] Money is `decimal`/`numeric`, VAT status explicit, unit in the type or the name
- [ ] Times stored UTC, tariff logic computed in Europe/Oslo, DST cases considered
- [ ] Scheme parameters read from the versioned table, not hard-coded
- [ ] Error paths return Problem Details and are user-legible
- [ ] Telemetry emitted for anything that can be slow or fail
- [ ] UI meets contrast, focus and reduced-motion requirements; axe passes
- [ ] Norwegian and English strings both present
- [ ] Migration reviewed for locks; rollback path known
- [ ] ADR written if a decision was made
- [ ] README/docs updated if behaviour changed

---

## 7. Anti-patterns specific to this project

Things that will happen if you're not watching for them:

1. **Spot price displayed as "your price".** Every number goes through the tariff engine. Add a lint rule: `price_ex_vat` may not be passed to a formatting function outside `Domain.Pricing`.
2. **A hard-coded `77` or `50`.** Grep for bare numeric literals in pricing code as part of review.
3. **`DateTime.Now` anywhere.** Banned by an analyzer rule; inject `TimeProvider`.
4. **Training on observed weather.** Guarded by the point-in-time test suite.
5. **Silent gap-filling.** Missing price hours must never be interpolated in the ingestion layer. The model may impute; the database may not.
6. **Chart library creep.** Once one screen uses a library, the visual language fractures. If a library is ever justified, it replaces the hand-written charts everywhere, by ADR.
7. **Building auth before there is personal data.** Delays every visible feature by two weeks and demos badly.
8. **Growing the model before the baselines are honest.** No neural anything until B0–B4 exist, the walk-forward harness runs, and the report is published.

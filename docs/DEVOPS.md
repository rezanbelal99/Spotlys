# DEVOPS.md — development and production practice

The principles this document applies, stated once so the rest of it reads as consequences:

1. **Everything as code.** Infrastructure, configuration, dashboards, alerts, database
   schema, runbooks. If it exists only in a web console or someone's shell history, it
   doesn't exist.
2. **Build once, deploy many.** One immutable artefact, promoted across environments.
   Environments differ by configuration, never by build.
3. **Fast feedback, shifted left.** The expensive failure is the one found in production.
   Every class of defect gets caught by the earliest mechanism that can catch it —
   compiler, then analyzer, then test, then CI, then staging, then alert.
4. **You build it, you run it.** The person who writes the ingestion job owns its alert
   and its runbook. As a solo project, that person is always you, which is the point.
5. **Everything reversible.** Every deployment has a rollback. Every migration has a
   backward-compatible path. Every model has a previous version to flip back to.
6. **Observable by default.** A system you can't see is a system you can only guess about.

---

## 1. Environments

| Environment | Purpose | Data | Deploy trigger |
|---|---|---|---|
| **local** | Inner loop | Seeded fixtures, no real personal data | `make up` |
| **ci** | Verification | Ephemeral Testcontainers | Every push |
| **staging** | Pre-production rehearsal | Real ingested market data, synthetic households only | Merge to `main` |
| **production** | The live app | Real everything | Manual promotion (tag) |

Staging runs the same manifests as production with a different namespace and a smaller
resource request. It exists for one reason: to run migrations and new ingestion jobs
against real upstream APIs before production does. Both live on the single k3s node; this
is a portfolio project, not a bank.

**Real household consumption data never leaves production.** No "copy prod to staging" —
that's a GDPR incident with extra steps. Staging is seeded with generated profiles.

---

## 2. Source control and branching

**Trunk-based.** Short-lived branches off `main`, merged within a day or two. No long-lived
`develop`. `main` is always deployable.

- Branch protection on `main`: no direct pushes, no force pushes, CI must pass.
- Conventional Commits, enforced by a commit-msg hook.
- Every merge to `main` deploys to staging automatically.
- Production releases are **tagged** (`v1.4.0`) and promoted manually. The tag is the
  release; the changelog is generated from commits since the previous tag.

### Pre-commit hooks (local, fast — under 5 seconds)

```
pre-commit:  dotnet format --verify-no-changes  |  eslint --max-warnings 0  |  secret scan (gitleaks)
commit-msg:  conventional commit shape
pre-push:    dotnet build -warnaserror  |  unit tests only
```

Integration tests do **not** run pre-push — a slow hook is a hook people disable.

### Claude Code hooks

`.claude/settings.json` turns the advisory rules in CLAUDE.md into deterministic ones:

```jsonc
{
  "hooks": {
    "PostToolUse": [
      { "matcher": "Edit|Write",
        "hooks": [{ "type": "command", "command": "dotnet format --include $CLAUDE_FILE_PATHS" }] }
    ],
    "PreToolUse": [
      { "matcher": "Bash",
        "hooks": [{ "type": "command", "command": ".claude/scripts/block-dangerous.sh" }] }
    ]
  }
}
```

`block-dangerous.sh` blocks `git push --force`, `git reset --hard` on `main`, `DROP TABLE`,
`kubectl delete` against the production namespace, and any `curl` to a host not on the
allowlist. CLAUDE.md suggests; hooks enforce. Anything that must hold 100 % of the time
belongs here, not in prose.

---

## 3. Configuration and secrets

- **Config through environment variables**, bound to strongly-typed options classes with
  `ValidateOnStart()`. The app refuses to boot on invalid configuration rather than failing
  at 13:00 on a Tuesday.
- `.env.example` lists every variable with a comment. It is the documentation.
- **Local:** `dotnet user-secrets`. **CI:** Gitea Actions secrets. **Production:** Kubernetes
  secrets, sealed with SOPS + age so the encrypted form is committed and the cluster state
  is reproducible from the repo.
- `gitleaks` in pre-commit and in CI. A secret in history means rotate the credential, not
  just rewrite the commit.
- Every upstream credential (ENTSO-E key, Frost key, VAPID keys) documented in
  `docs/runbooks/credentials.md` with where it came from and how to rotate it.

---

## 4. Build and artefacts

- **Multi-stage Dockerfiles**, non-root user, distroless or `-alpine` runtime base, no build
  toolchain in the final image.
- Images tagged with the **git SHA**, plus a semver tag on release. `latest` is never
  deployed from.
- **SBOM generated** (`syft`) and attached to every image. **Trivy scan** in CI; high or
  critical findings fail the build.
- Docker layer caching on the self-hosted runner; NuGet and npm caches persisted between runs.
- Reproducibility: pinned base image digests, `packages.lock.json` committed, `npm ci` not
  `npm install`, central package management for NuGet.
- The ONNX model is **not** baked into the image. It's an artefact in object storage
  referenced by `model_version`, so a model rollback doesn't require a redeploy.

---

## 5. Pipeline

```
push ──► ci.yml
         format → build (warnings as errors) → unit → integration (Testcontainers)
         → architecture tests → point-in-time tests → coverage gate → frontend lint/type/test/build
                │
         merge to main ──► container.yml
                           build image → SBOM → Trivy → push → deploy staging → smoke test
                │
         tag v* ──────────► promote same image digest to production → smoke test → notify
```

Separate scheduled workflows: `model.yml` (weekly retrain + backtest gate),
`freshness.yml` (every 30 min, probes `/api/v1/status`), `restore-test.yml` (monthly
backup restore verification).

**Smoke test after every deploy** — hits `/health/ready`, `/api/v1/status`, and fetches
today's prices for NO2, asserting a non-empty response. A deploy that isn't verified isn't
a deploy. Failure triggers automatic rollback to the previous image digest.

Pipeline principles: the build produces the artefact once and every later stage promotes
that exact digest; no stage rebuilds; any stage can fail the release; the whole thing runs
on your own runner so it costs nothing and you own the failure modes.

---

## 6. Database migrations — the riskiest thing you do

- Migrations run in a **dedicated init container / job**, never at API startup. An API that
  migrates on boot will, on the day you scale to two replicas, run two migrations at once.
- **Expand / contract**, always. Add the nullable column and backfill; deploy code that
  writes both; deploy code that reads the new; drop the old in a *later* release. Never a
  destructive change in the same deploy as the code that needs it.
- Every migration reviewed for lock behaviour. `CREATE INDEX CONCURRENTLY` on anything
  covering the time-series tables.
- A migration is not done until the **rollback path is written down in the PR**, even if the
  rollback is "restore from backup" — that's an acceptable answer, but it has to be a
  stated one.
- Backfills run as one-shot jobs with explicit windows, never inside a migration.

---

## 7. Observability and SLOs

Instrumented with OpenTelemetry → Prometheus, Grafana, Loki. Dashboards committed as JSON
in `deploy/grafana/`; alert rules committed in `deploy/prometheus/rules/`.

### Service level objectives

| SLO | Target | Why it's the right number |
|---|---|---|
| Today's prices available and fresh | 99.5 % of hours | The core promise of the app |
| Day-ahead ingested by 14:30 Oslo | 99 % of days | Publication is ~13:00; 90 min of slack |
| `GET /forecast` p95 latency | < 300 ms | Cached; anything slower means the cache is broken |
| Forecast job completes by 06:15 / 13:30 | 99 % | Notifications at 20:00 depend on it |
| Production MAE within 25 % of backtest MAE | 7-day rolling | Concept drift detector |

### Alerts — each one has a runbook, or it doesn't exist

| Alert | Severity | Runbook |
|---|---|---|
| Day-ahead not ingested by 14:30 | page | `runbooks/price-feed-stale.md` |
| Any feed beyond its `MaxStaleness` | warn | `runbooks/feed-lag.md` |
| Forecast job failed | warn | `runbooks/forecast-job-failed.md` |
| Production MAE drift > 25 % | warn | `runbooks/model-drift.md` |
| API 5xx rate > 1 % over 5 min | page | `runbooks/api-errors.md` |
| Certificate expiring < 14 days | warn | automated renewal; alert means renewal broke |
| Disk > 80 % | warn | `runbooks/disk.md` |

Alert discipline: **an alert that fires without requiring action gets deleted or its
threshold fixed, the same week.** Alert fatigue on a solo project kills the whole
observability effort within a month.

### Runbook format

Four sections, one page: *what the alert means* / *how to confirm* / *how to fix* / *how to
prevent recurrence*. Written when the alert is created, improved every time it fires.

### User-facing honesty

The `/api/v1/status` endpoint and the banner at the top of the UI expose feed freshness
directly. If ingestion is broken, users are told before they're shown a number. Internal
observability and user-facing status come from the same `ingestion_run` table — one source
of truth, so the dashboard can't disagree with the app.

---

## 8. Backups and recovery

- Nightly `pg_dump` to object storage, encrypted, 30 daily + 12 monthly retained.
- **Monthly automated restore test** (`restore-test.yml`) that provisions a throwaway
  container from the latest dump and asserts table row counts and one known invoice
  calculation. An untested backup is a belief, not a backup.
- **Stated RPO/RTO: 24 hours / 2 hours.** Documented in `runbooks/disaster-recovery.md`,
  which includes the full rebuild-from-zero procedure: provision node → apply manifests →
  restore dump → verify. Rehearse it once, at the end of Phase 5.
- Market data is re-ingestible from upstream, so the irreplaceable data is small: user
  accounts, meter profiles and imported consumption. Those are the tables that matter.

---

## 9. Local development loop

The target: **clean clone to running, seeded, useful system in under five minutes with one
command.** This is not a nicety — it's what makes the project survivable when you come back
to it after exams.

```bash
git clone … && cd spotlys && make bootstrap
```

`make bootstrap` checks toolchain versions, starts the compose stack, applies migrations,
loads a fixture dataset (90 days of real prices, three grid companies' tariffs, one
synthetic household, a pre-trained ONNX model), and prints the URLs.

- **Hot reload** both sides: `dotnet watch`, Vite HMR.
- **Seeded fixtures beat live APIs** in development — deterministic, offline-capable, and
  you stop burning MET's goodwill during a refactor. A `USE_LIVE_FEEDS=true` flag opts in.
- **Devcontainer** (`.devcontainer/`) pinning the .NET SDK, Node, Python and Docker
  versions so the environment is the same on your laptop, the runner and any machine you
  borrow.
- A `docs/ONBOARDING.md` written as if for a new contributor. There is no new contributor;
  it's for you in March, and reviewers read it as a signal.

---

## 10. Cost and capacity

- Target under 150 kr/month: one DigitalOcean droplet, managed DNS, object storage for
  backups and model artefacts. Everything else is free-tier or self-hosted.
- Resource requests and limits set on every pod. An unbounded ingestion job will eventually
  OOM the node that also runs your database.
- Upstream API budgets are explicit and enforced in code: ~25 weather points × 4 refreshes
  per day, price polling with backoff and conditional requests. Getting rate-limited by a
  free public API you depend on is self-inflicted.
- Cost is stated in the README. Running something real, cheaply, is a signal.

---

## 11. What "production-ready" means for this project

A deliberately scoped definition — don't gold-plate toward a scale that doesn't exist:

- One node, no HA. Documented as a conscious trade-off with the recovery procedure that
  compensates for it.
- No autoscaling. Load is a handful of users and two cron-driven forecast runs.
- No blue/green. Rolling update with a smoke test and automatic rollback on failure.
- No paging rota. Alerts go to your phone; the SLOs assume a human who sometimes sleeps.

Write that list down in the README under "Operational trade-offs". Knowing which
production practices you deliberately skipped, and why, reads better in an interview than
pretending you built Netflix.

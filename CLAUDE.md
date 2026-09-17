# CLAUDE.md

Spotlys — Norwegian electricity price forecasting and household bill optimisation.
Read this file every session. When it conflicts with a general instinct, this file wins.

## What this is

A forecasting engine (day-ahead prices, D+2 to D+7, quantile regression) wrapped in a
correct Norwegian tariff model, used to tell a specific household what to do and whether
it matters for them. The forecast is the engine, not the product.

## Reference documents — read before working in the matching area

@docs/DOMAIN.md — Norwegian market, tariffs, support schemes. **Read before any pricing work.**
@docs/DATA.md — sources, licences, schemas, ingestion contract
@docs/FORECASTING.md — targets, features, baselines, metrics, backtesting, optimizer
@docs/ARCHITECTURE.md — services, solution layout, database, jobs, API
@docs/DESIGN.md — visual identity, layout, motion, accessibility
@docs/ENGINEERING.md — testing strategy, CI gates, definition of done
@docs/DEVOPS.md — environments, releases, secrets, observability, runbooks
@docs/ROADMAP.md — phases and their done-criteria
@docs/adr/ — decisions already made. Do not relitigate one without writing a superseding ADR.

## Hard rules — violating any of these is a defect, not a style choice

1. **Never display a raw spot price as the user's price.** Every number the user sees goes
   through `Spotlys.Domain.Pricing.TariffEngine`.
2. **Money is `decimal` / `numeric`.** Never `float`/`double`. Unit and VAT status must be
   in the type or the name (`priceExVatOrePerKwh`, not `price`).
3. **Scheme parameters are data.** The strømstøtte threshold, the Norgespris rate, caps,
   VAT and levy rates come from the `scheme_parameter` table with an effective-from date
   and a source URL. A bare `77` or `50` in pricing code fails review.
4. **Point-in-time correctness.** A feature used for a forecast issued at `T` may only read
   rows where `issued_at_utc <= T`. No training on weather observations. Ever.
5. **Store UTC, compute tariffs in Europe/Oslo.** Night rates, daily peaks and calendar days
   are local concepts. Both DST transition days must work.
6. **No `DateTime.Now` / `DateTime.UtcNow`.** Inject `TimeProvider`.
7. **Never silently fill gaps.** Missing price hours stay missing in the database. The model
   may impute; the ingestion layer may not.
8. **Never invent a saving figure.** Ranges from the quantile fan, plus a counterfactual.
9. **Attribution stays.** hvakosterstrommen.no credit in the footer; the MET `User-Agent`
   with real contact details on every request to api.met.no.
10. **No secrets in the repo.** Ever. `.env.example` documents variables; values come from
    user-secrets locally and Kubernetes secrets in production.

## Working agreement

- **Plan before writing.** For anything beyond a one-file change, use plan mode
  (Shift+Tab), propose the approach, wait for approval. Say which files you'll touch.
- **One vertical slice per session.** Ingestion → storage → API → UI for one capability,
  not a whole layer across all capabilities.
- **Tests in the same change as the code**, at the layer ENGINEERING.md specifies. Domain
  logic gets property or golden-file tests, not example tests that restate the implementation.
- **Stop and ask** when a decision affects the domain model, the database schema, a public
  API contract, or costs money. Don't guess at Norwegian regulation — ask me, I work in
  this market.
- **Small commits, Conventional Commits, one logical change each.** Never `git push --force`
  to `main`. Never commit without the build and tests passing locally.
- **If you're about to write a workaround, say so out loud** and propose the real fix first.
- **Don't add dependencies casually.** Justify any new package in the PR description. No
  charting library (ADR 0004), no state management library, no ORM other than EF Core.

## Commands

```bash
make bootstrap        # clean clone → running stack with seeded fixtures
make up               # docker compose up (api, worker, db, web, grafana, prometheus, loki)
make test             # dotnet test + vitest
make test-integration # Testcontainers suite (needs Docker)
make lint             # dotnet format --verify-no-changes + eslint + tsc --noEmit
make migrate name=X   # add an EF migration
make backtest         # python walk-forward backtest, writes out/report.md
make check            # everything CI runs, in CI order. Run before every push.
```

`make check` must pass before you tell me something is done.

## Definition of done

Copy this into every PR description and tick it honestly:

- [ ] Builds clean with warnings-as-errors
- [ ] Tests at the right layer; new domain logic has property or golden-file coverage
- [ ] No feature reads data that would not have existed at its `as_of`
- [ ] Money `decimal`, VAT explicit, units named
- [ ] UTC stored, Europe/Oslo computed, DST considered
- [ ] Scheme parameters from the versioned table
- [ ] Errors return Problem Details and are legible to a user
- [ ] Telemetry on anything that can be slow or fail
- [ ] UI: contrast, visible focus, reduced motion, axe passes
- [ ] Norwegian and English strings both present
- [ ] Migration reviewed for locks; rollback path stated
- [ ] ADR written if a decision was made
- [ ] `make check` green

## Current phase

> Update this line at the start of each phase. Claude reads it to scope its work.

**Phase 0 — spike.** Goal: prove the data sources and reproduce one real invoice by hand.
Do not scaffold the full solution yet.

## Things that have bitten this project before

> Append to this list whenever we hit something twice. This section is the point of the file.

- MET blocks generic `User-Agent` strings and requires honouring cache headers.
- hvakosterstrommen prices are **excluding VAT**, and NO4 pays no VAT at all.
- Tomorrow's prices don't exist before ~13:00. Absence before 14:30 is normal, not an error.
- Above the strømstøtte threshold the state covers 90 %, so the marginal saving from
  avoiding an expensive hour is a tenth of the raw spread. Don't overstate savings.
- `TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo")` fails on Alpine without ICU. Set
  `InvariantGlobalization=false` and install `tzdata`.

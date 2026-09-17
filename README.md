# Spotlys

**Forecasts Norwegian electricity prices, then tells you what to actually do about it — for your tariff, your grid company, your consumption.**

Spotlys is a portfolio-grade engineering project: a production-shaped system with a genuinely hard modelling core. It ingests day-ahead prices, weather forecasts and hydrology, forecasts hourly area prices 2–7 days ahead with calibrated uncertainty, models the *whole* Norwegian electricity bill (spot + påslag + nettleie + strømstøtte or Norgespris), and schedules flexible load against it.

---

## 1. The product thesis

Most "strømpris app" projects stop at: fetch spot price, draw a bar chart, colour the cheap hours green. That app is wrong for a large share of Norwegian households in 2026, for one reason:

> **Norgespris.** Since 1 October 2025, households can lock electricity at a flat 50 øre/kWh (incl. VAT) for up to 5 000 kWh/month, replacing the ordinary strømstøtte. The scheme is now in force through 2029, with the price level to be adjusted from 1 January 2027. For anyone on Norgespris, the hourly spot price is **irrelevant** below the monthly cap — moving the dishwasher to 03:00 saves exactly zero kroner on the energy line.

If the app tells those users to shift load to save on spot, the app is lying to them. But shifting load still saves them money, for a different reason, and almost no consumer app explains it clearly:

1. **Kapasitetsledd** — the grid tariff's capacity term is billed on the *average of the three highest hourly consumption peaks* in the month, on three different days. Dropping one step is worth roughly 1 500–1 900 kr/year. This is entirely independent of Norgespris.
2. **Energiledd, day/night** — grid companies charge a lower per-kWh rate on weekday nights (22:00–06:00) plus all weekends and public holidays. Typically 5–15 øre/kWh cheaper. Also independent of Norgespris.
3. **Above the cap** — consumption over 5 000 kWh/month (heat pump + EV + cold snap) falls back to market price with no support at all, which is exactly when spot matters most and exactly when a forecast is most valuable.
4. **The regime choice itself** — "should I be on Norgespris or spot + strømstøtte?" is a real, high-stakes, poorly-served question. Answering it requires *your* consumption profile and a *price forecast*. That's the whole product in one sentence.

**So Spotlys forecasts prices in order to compute the bill, and computes the bill in order to give advice that survives contact with a real Norwegian invoice.** The forecast is the engine, not the product.

### The three user questions

| Question | What it needs | Where it's answered |
|---|---|---|
| "When should I charge the car this week?" | Price forecast + tariff model + capacity-peak state | Plan view |
| "Am I about to blow into a higher kapasitetstrinn?" | Live consumption + month-to-date peaks | Peak guard |
| "Norgespris or spot — which one for me?" | Historical consumption + forward price forecast | Regime advisor |

### Non-goals

- Not a comparison site for electricity suppliers. No affiliate links, no lead-gen.
- Not financial advice. The regime advisor shows a distribution of outcomes and its own error bars, and says so.
- Not a home automation controller (Phase 1). It recommends; it doesn't switch relays. Control is an explicit Phase 5 stretch.
- Not trying to beat Volue or Statkraft at price forecasting. The bar is: beat a well-specified naive baseline, honestly measured, with calibrated uncertainty.

---

## 2. Why this is a good project to be judged on

| Signal a reviewer looks for | How Spotlys produces it |
|---|---|
| Can they ship? | Deployed, public URL, seeded demo mode, no login required to look |
| Do they understand data over time? | Point-in-time correct feature store; forecasts stored *as issued* |
| Can they evaluate a model honestly? | Walk-forward backtest in CI; a regression in MAE fails the build |
| Do they understand the domain? | Full tariff model incl. kapasitetsledd, strømstøtte, Norgespris, VAT/NO4 |
| Can they write maintainable code? | Nullable-strict, warnings-as-errors, Testcontainers integration tests, ADRs |
| Can they run it? | OpenTelemetry traces, freshness alerts, one-command local bring-up |
| Do they have taste? | A UI you can read in three seconds in a dark kitchen at 22:30 |

The single strongest interview story in this project: **"my model looked great until I discovered I was training on weather observations instead of the weather forecast that was available at the time, so I rebuilt the feature store to be point-in-time correct and my MAE got 30 % worse — and honest."**

---

## 3. Document map

| File | Contents |
|---|---|
| `docs/DOMAIN.md` | Norwegian electricity market, tariffs, support schemes. Read this first. |
| `docs/DATA.md` | Every data source, its licence, cadence, schema, failure modes |
| `docs/FORECASTING.md` | Targets, features, baselines, metrics, backtesting, serving |
| `docs/ARCHITECTURE.md` | Services, .NET solution layout, database schema, jobs, deployment |
| `docs/DESIGN.md` | Visual identity, layout, component and motion specification |
| `docs/ENGINEERING.md` | Code quality standards, testing strategy, CI/CD, observability |
| `docs/ROADMAP.md` | Phased plan, week by week, with done-criteria per phase |
| `docs/adr/` | Architecture decision records |

---

## 4. Stack at a glance

- **Backend** — .NET 10, ASP.NET Core Minimal APIs, EF Core 10, PostgreSQL 17
- **Ingestion / scheduling** — .NET Worker Service, Quartz.NET
- **Modelling** — Python 3.12, LightGBM, exported to ONNX; inference in-process via ONNX Runtime for .NET
- **Frontend** — React 19, TypeScript (strict), Vite, D3 scales with hand-written SVG, Motion for animation
- **Infra** — Docker Compose → k3s, Gitea Actions on a self-hosted runner, Watchtower, Prometheus + Grafana + Loki

Rationale for every one of these choices, including the ones that look odd (hand-written SVG instead of a chart library; Python training but .NET serving), is in the relevant doc and in `docs/adr/`.

---

## 5. Name

*Spotlys* — spotpris + lys, and it reads as "spotlight" in Norwegian. The product shines a light on a specific hour. Domain `spotlys.no` or `spotlys.app`.

# DATA.md — sources, licences, schemas, failure modes

Rule for this project: **every ingested record stores the time it was observed by us, separately from the time it refers to.** No exceptions. FORECASTING.md §5 explains why this is non-negotiable.

---

## 1. Day-ahead prices

### Primary: hvakosterstrommen.no

```
GET https://www.hvakosterstrommen.no/api/v1/prices/{YYYY}/{MM}-{DD}_{NOx}.json
```

- Free, open, no key, static JSON files. Historical data back to 1 September 2022.
- Prices in the JSON are **excluding VAT**. Sourced from ENTSO-E in EUR and converted with the Norges Bank rate, exposed as an `EXR` field.
- Tomorrow's file appears at the earliest around 13:00 the day before.
- Timestamps are ISO 8601 with offset.
- **Attribution is expected** — the site asks that you credit them. Put their badge in the footer and a line in the README. Free data with a courtesy request is the cheapest ethical obligation you will ever meet; honour it.

Ingestion job:
- Backfill: one request per zone per day, 5 zones × ~1 500 days ≈ 7 500 requests. Rate-limit yourself to ~2 req/s, run once, cache to disk, never re-run.
- Daily: poll from 12:45, exponential backoff, stop on success. Alert only if not present by 14:30.
- Conditional requests (`If-Modified-Since`) on repeat polls.

### Secondary / verification: ENTSO-E Transparency Platform

Register for a free API key. Use it for (a) cross-checking hvakosterstrommen values, (b) neighbouring-zone prices (SE3, SE4, DK1, DK2, DE-LU, FI, NL, GB) which are strong features, (c) cross-border flows and load. Higher operational burden (XML, quirky query semantics), so it belongs in Phase 3, not Phase 1.

### Storage

```sql
CREATE TABLE price_observation (
    zone            text        NOT NULL,        -- 'NO1'..'NO5','SE3',...
    hour_start_utc  timestamptz NOT NULL,
    price_ex_vat    numeric(10,4) NOT NULL,      -- øre/kWh
    currency_rate   numeric(10,6),               -- EUR->NOK used
    source          text        NOT NULL,        -- 'hkso' | 'entsoe'
    observed_at_utc timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (zone, hour_start_utc, source)
);
CREATE INDEX ON price_observation USING brin (hour_start_utc);
```

`observed_at_utc` matters even here: ENTSO-E occasionally revises published values.

---

## 2. Weather

### Forecasts: MET Norway Locationforecast 2.0

```
GET https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={lat}&lon={lon}&altitude={m}
```

- Free, no API key. **A descriptive `User-Agent` identifying the application and a contact address is mandatory** — generic agents get blocked. Use e.g. `spotlys/1.0 (https://spotlys.no; rezan@…)`.
- Terms of service require honouring cache headers. Send `If-Modified-Since`, respect `Expires`, and never re-request unexpired data. Non-compliance gets you banned, and being banned from your own data source mid-demo is a bad story.
- `complete` gives temperature, wind speed and direction, cloud fraction, precipitation, pressure. `compact` is a reduced set.
- Lat/lon must be rounded to four decimals; altitude to the nearest integer.

**Sampling strategy.** Don't fetch a national grid. Pick **4–6 representative points per price zone** — a population-weighted point (demand proxy), a coastal/wind point, and a hydrological catchment point — and aggregate. ~25 points × 4 refreshes/day = 100 requests/day, trivially within the courtesy budget.

### Observations: Frost API (frost.met.no)

Free, key required, quality-controlled historical observations. Used **only** for backfilling the training set for the period before Spotlys existed, and clearly flagged as such — an observation-derived feature and a forecast-derived feature are not the same variable (see FORECASTING.md §5).

### Storage — the important part

```sql
CREATE TABLE weather_forecast (
    point_id        text        NOT NULL,
    issued_at_utc   timestamptz NOT NULL,   -- when MET published this run
    fetched_at_utc  timestamptz NOT NULL,   -- when we retrieved it
    valid_at_utc    timestamptz NOT NULL,   -- the hour it describes
    temp_c          real,
    wind_ms         real,
    wind_dir_deg    real,
    cloud_frac      real,
    precip_mm       real,
    PRIMARY KEY (point_id, issued_at_utc, valid_at_utc)
);
```

Three separate timestamps. A feature built for a forecast issued at time *T* may only read rows where `issued_at_utc <= T`. This single constraint is what separates a model with an honest MAE from a model with a fantasy MAE.

---

## 3. Hydrology and system state

Norwegian prices are hydro prices. Reservoir levels are the slow-moving state variable that sets the level around which weather moves things.

| Source | What | Cadence | Notes |
|---|---|---|---|
| NVE — reservoir statistics (fyllingsgrad) per elspot area | Reservoir fill %, deviation from median | Weekly (Wednesdays) | Open data; the single most explanatory slow feature |
| NVE Hydrologi / snow (snømagasin) | Snow water equivalent | Weekly, seasonal | Explains spring flood expectations |
| ENTSO-E | Cross-border scheduled flows, load, wind/solar forecast for DE/DK | Hourly | Phase 3 |
| Norges Bank | EUR/NOK reference rate | Daily | Already embedded in hvakosterstrommen's `EXR`, but needed independently for ENTSO-E-sourced series |

Weekly features must be **forward-filled with their own as-of date**, never interpolated backwards from a later publication.

---

## 4. The user's own consumption

This is the hardest data to get and where most hobby projects quietly give up. Be explicit about the ladder, and ship the bottom rungs first.

| Option | Feasibility for a student | Latency | Verdict |
|---|---|---|---|
| **CSV import** from grid company's Min Side | Trivial | D+1, manual | **Phase 1.** Ships immediately, works for everyone |
| **HAN port reader** (ESP32 + M-Bus, or a Tibber Pulse) publishing to MQTT | Moderate; needs hardware ~300–600 kr | ~2–10 s | **Phase 4.** Unlocks live peak guard. Great demo |
| **Tibber API** (if the user is a Tibber customer) | Easy, GraphQL, good docs | ~live | **Phase 4**, optional integration |
| **Elhub third-party access** | Not realistic | D+1 | Requires being a registered market actor with a GLN and enterprise certificate; individual developers cannot get it. Document the finding, don't chase it |
| **Elhub open aggregated datasets** | Easy | Monthly-ish | Useful as a *population* prior for cold-start consumption profiles, not per-user |

Elhub's consumer-facing access management does let a person grant a registered third party access to their meter data — the blocker is becoming a registered third party, not the consent flow. Write this up as an ADR: it's exactly the kind of "I investigated the ideal path, found the institutional barrier, and shipped the pragmatic path" reasoning interviewers respond to.

**Cold start without any consumption data:** synthesise a profile from household archetype (apartment / detached, heating type, EV yes/no, number of residents) × Elhub's public aggregated zone profiles × heating-degree-days. Label it clearly in the UI as an estimate and prompt for a CSV import to replace it.

### Storage and privacy

Metered consumption is personal data under GDPR. Therefore:
- Store per-meter series in a separate schema with its own retention policy (default 3 years, matching Elhub's own retention for private customers, with user-set extension).
- Export and delete endpoints from day one, not retrofitted.
- Never send consumption to third parties. No analytics on consumption values.
- The privacy page is written before the feature ships, not after.

---

## 5. Calendar

Norwegian public holidays including movable feasts, plus school holidays as a secondary feature. Use a maintained library (`Nager.Date` for .NET, or `holidays` in Python) and **pin the version**; verify against a published Norwegian calendar in a unit test for the next three years. Holidays matter twice: they shift demand, and they change the nettleie energiledd rate.

---

## 6. Ingestion contract

Every ingestion job implements the same interface and the same guarantees:

```csharp
public interface IIngestionJob
{
    string   Name          { get; }
    TimeSpan MaxStaleness  { get; }      // drives the freshness alert
    Task<IngestionResult> RunAsync(DateTimeOffset asOf, CancellationToken ct);
}
```

- **Idempotent.** Re-running for the same window produces the same rows. Upsert on the natural key.
- **Bounded.** Every job takes an explicit window; no job means "fetch everything".
- **Observed.** Emits `spotlys.ingest.rows`, `spotlys.ingest.duration`, `spotlys.ingest.lag_seconds`.
- **Honest on failure.** A partial fetch is recorded as partial, never silently padded. Missing hours stay missing; the model handles gaps, the database does not lie about them.
- **Rate-limited and identified.** A named `HttpClient` per upstream, with Polly retry + circuit breaker and the required `User-Agent`.

### Freshness as a first-class concept

```sql
CREATE TABLE ingestion_run (
    job_name    text NOT NULL,
    started_at  timestamptz NOT NULL,
    finished_at timestamptz,
    status      text NOT NULL,        -- ok | partial | failed
    rows        integer,
    window_from timestamptz,
    window_to   timestamptz,
    error       text
);
```

The UI reads this. If the price feed is stale, the dashboard says so at the top, in plain language, before showing any number. **A dashboard that silently shows yesterday's data is a broken dashboard.**

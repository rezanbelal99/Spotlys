# DOMAIN.md — the Norwegian electricity bill, precisely

Everything in this file is a requirement on the tariff engine. Get this wrong and the app gives confidently incorrect advice, which is worse than no app.

> **Currency and units.** Internally, everything is stored in **øre/kWh as `numeric(10,4)`, excluding VAT**, with an explicit VAT flag applied at presentation. Never store money as `float`. Never store a price without knowing whether VAT is in it — this is the single most common bug in Norwegian energy apps.

---

## 1. Price areas

Norway is split into five bidding zones. Prices between them can differ by an order of magnitude.

| Zone | Region | Notes |
|---|---|---|
| NO1 | Sørøst-Norge (Oslo) | |
| NO2 | Sørvest-Norge (Kristiansand, Stavanger) | Interconnectors to DE/NL/DK/UK — most continental coupling |
| NO3 | Midt-Norge (Trondheim) | |
| NO4 | Nord-Norge (Tromsø) | **No VAT on electricity.** Structurally low prices |
| NO5 | Vestlandet (Bergen) | |

Implications:
- **NO4 is a separate VAT regime.** Nordland, Troms and Finnmark pay no VAT on electricity. The tariff engine must take VAT rate as a function of the delivery address, not a constant.
- A user is bound to a zone by their meter, so zone is part of the user's profile, not a UI filter.
- Zones are highly correlated but not interchangeable; the model is trained per zone (see FORECASTING.md §3).

---

## 2. The day-ahead market and the publication clock

Spot prices come from the day-ahead auction. **Tomorrow's prices are published at the earliest around 13:00 CET the day before.** hvakosterstrommen.no sources from ENTSO-E in EUR and converts to NOK using the Norges Bank rate for the day, so its figures can differ slightly from Nord Pool's own NOK figures while following the same underlying curve.

This clock is the most important constraint in the entire system:

```
        t=00:00 ─────────────── 13:00 ──────────────── 24:00
  D+1:  forecast territory  │   KNOWN (auction cleared)
  D+2:  forecast territory  │   forecast territory
  D+7:  forecast territory  │   forecast territory
```

Consequences the design must respect:

1. **Never forecast what is already known.** Before ~13:00, D+1 is a forecast target. After publication, D+1 is ground truth and the UI must visually switch from "forecast" to "confirmed". Two different model regimes, two different UI states.
2. **The interesting horizon is D+2 to D+7.** That's where no free consumer app gives you anything, and where a weekly EV-charging plan actually lives.
3. **Publication can be late or partial.** The ingestion job must poll with backoff from 12:45, treat absence as normal until ~14:30, and alert only after that.
4. **Model evaluation must be issue-time anchored.** "MAE at 36-hour lead time" is a meaningful number. "MAE" alone is not.

---

## 3. Support schemes: strømstøtte vs Norgespris

Two mutually exclusive regimes, per meter. The tariff engine implements both.

### 3a. Ordinary strømstøtte (the default)

- Calculated **hour by hour** since 1 September 2023 — not on a monthly average.
- The state covers **90 % of the price above the threshold**.
- Threshold for 2026: **77 øre/kWh excluding VAT** (96.25 øre/kWh including VAT), adjusted in the 2026 national budget from the previous 75 øre.

```
støtte(h) = 0.90 × max(0, spot_ex_vat(h) − threshold_ex_vat)
net_energy_cost(h) = (spot_ex_vat(h) + påslag − støtte(h)) × (1 + vat_rate)
```

Under this regime, hour-shifting **does** save money on the energy line — but note the support flattens the top: above the threshold, 90 % of any further increase is absorbed by the state, so the *marginal* benefit of avoiding an expensive hour is only 10 % of the spread once both hours are above the threshold. A naive "avoid the 2.30 kr hour, use the 0.90 kr hour" saving figure overstates the real benefit by up to 10×. **The tariff engine computes marginal cost after support, never raw spot spread.** This is a genuine differentiator and a good interview point.

### 3b. Norgespris

- Voluntary, ordered by the customer via Elhub, administered by the grid company like strømstøtte.
- **50 øre/kWh incl. VAT (40 øre ex. VAT)** for 2026, uniform across all zones. Price level to be adjusted from 1 January 2027; scheme adopted through 2029.
- **Symmetric fixed price, not a cap.** If spot falls to 10 øre you still pay the Norgespris level — you forgo cheap hours as well as expensive ones.
- Caps: **5 000 kWh/month for homes, 1 000 kWh/month for cabins.** Consumption above the cap is billed at your ordinary market agreement with **no support at all**.
- Binding to the meter, not the person, with a lock-in period.
- Replaces strømstøtte entirely — you cannot have both.

Modelling consequences:

```
if norgespris and month_to_date_kwh + kwh(h) <= cap:
    energy_cost(h) = norgespris_rate × kwh(h)          # spot-independent
else:
    energy_cost(h) = (spot_ex_vat(h) + påslag) × kwh(h) × (1+vat)   # no støtte above cap
```

The cap creates a **within-month regime switch**: a household on Norgespris with an EV and a heat pump can cross 5 000 kWh mid-January and suddenly become fully spot-exposed with zero support. Spotlys should see that coming days in advance and say so. Nobody else does this. It is the best single feature in the product.

---

## 4. Nettleie (the grid tariff) — where savings survive Norgespris

Since July 2022 the household grid tariff has two parts plus public levies.

### 4a. Kapasitetsledd

A monthly amount set by a step model. The step is determined by **the average of the three highest hourly consumption values ("døgnmaks") in the month, taken from three different days.** Steps are typically 0–2, 2–5, 5–10, 10–15 kW and up; most customers sit in the lowest three or four. The gap between adjacent steps is on the order of 100–160 kr/month.

This is the highest-leverage, lowest-awareness item on a Norwegian electricity bill, and it is **completely unaffected by Norgespris**.

Product implications:
- The system must track **month-to-date top-3 peak hours per meter** and know the user's grid company step table.
- The optimizer's constraint is not "charge in the cheapest hours" but "charge in the cheapest hours **without creating a fourth peak above the current third-highest**".
- A "peak guard" alert — *you are 40 minutes into an hour at 6.2 kW; staying there costs you a step* — is genuinely useful and technically interesting (it needs live consumption, see DATA.md §4).
- Spreading an EV charge over eight hours at low power beats four hours at high power, even at identical energy prices. The optimizer must be able to express that.

### 4b. Energiledd, day/night differentiated

Per-kWh, with a lower rate for **weekday nights 22:00–06:00, all Saturdays, Sundays and public holidays**. Typically 5–15 øre/kWh cheaper at night. Also unaffected by Norgespris.

Requires a **Norwegian public-holiday calendar** — movable feasts included (Skjærtorsdag, Langfredag, 2. påskedag, Kristi himmelfartsdag, 2. pinsedag). Do not hand-roll this; see DATA.md §5.

### 4c. Levies (inside nettleie)

- **Forbruksavgift (elavgift)** — per kWh, set by the Storting, changes annually and sometimes mid-year (a reduced winter rate has existed in past years). Must be a time-versioned table, not a constant.
- **Enova levy** — a fixed annual amount per household.
- **VAT 25 %** on everything, except in NO4.

Regulatory note worth putting in the README of the tariff module: from 1 July 2026 the energy term may account for at most 50 % of a grid company's tariff revenue per customer group, which shifts weight toward the capacity term over time — i.e. the peak-avoidance feature gets *more* valuable, not less.

---

## 5. The complete bill

```
Monthly bill =
    Σ_h energy_cost(h)                        # spot ± regime, incl. påslag, støtte/Norgespris
  + Σ_h kwh(h) × energiledd(h)                # day/night rate
  + kapasitetsledd(step(mean(top3 døgnmaks)))
  + Σ_h kwh(h) × forbruksavgift(t)
  + enova_fixed / 12
  + supplier monthly fee
  ± VAT by zone
```

Every number Spotlys shows a user must come out of this function. No screen anywhere is allowed to display a raw spot price as if it were the price the user pays.

---

## 6. What the advice actually is, per user segment

| Segment | Spot shifting worth it? | Primary advice |
|---|---|---|
| Norgespris, under cap, no EV | No | Peak avoidance only. Be honest: "your energy price is flat; here's where your money actually goes" |
| Norgespris, near/over cap | Yes, above cap | Cap forecast + shift the marginal kWh + peak avoidance |
| Spot + strømstøtte, NO4 | Yes | Full spot optimisation (NO4 prices sit well below the støtte threshold most of the year, so support rarely blunts the spread) |
| Spot + strømstøtte, NO1/NO2/NO5 winter | Partly | Marginal-cost-after-support optimisation + peak avoidance |
| Cabin owner | Mixed | Lower cap (1 000 kWh); frost-protection scheduling |

The regime advisor's job is to place the user in one of these rows and then shut up about the irrelevant advice. **An app that gives the same five tips to everyone is the thing being replaced here.**

---

## 7. Honesty constraints

1. Savings estimates are shown as a range derived from forecast uncertainty, never a single hero number.
2. Any claim about a past month is computed from actual metered consumption, not from the forecast.
3. The regime advisor states its assumptions inline (your last 12 months of consumption, current forward view, current scheme parameters) and links to Elhub for the actual decision.
4. Scheme parameters (threshold, Norgespris level, caps, VAT, levy rates) live in a **versioned, dated configuration table** with a source URL and an effective-from date. They change by political decision, not by engineering timeline. Hard-coding 77 or 50 anywhere in application code is a review-blocking defect.

# spike/ — Phase 0

**Nothing in this folder ships.** It exists to de-risk the two things that could sink the
project before any real scaffolding happens: the data sources, and the domain model
(reproducing one real invoice by hand). See `docs/ROADMAP.md` Phase 0.

Once Phase 1 starts, this folder is deleted or cherry-picked into the real solution layout
in `docs/ARCHITECTURE.md` §2 — it is not itself part of that layout.

## Setup

```bash
cd spike
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

## Run, in order

```bash
python fetch_prices.py      # 12 months of NO2 day-ahead prices -> out/no2_prices_12mo.csv
python fetch_weather.py     # one MET Locationforecast point -> out/met_forecast_sample.json
jupyter nbconvert --to notebook --execute forecast_baseline.ipynb   # B1 + first LightGBM
python invoice_reproduction.py   # domain-rules skeleton; prints instructions, no invoice available
python invoice_fixture_run.py    # real rates/prices + a labelled SYNTHETIC consumption shape
python invoice_elhub_run.py      # real rates/prices + REAL Elhub Open Data consumption
                                  # (NO2 household average) -- needs data/elhub_open/, see below
```

Everything under `cache/` is a raw on-disk response cache so re-running a script never
re-fetches from the network. Safe to delete; it'll just refetch (respecting the same rate
limits and cache headers).

## VAT convention used here

- hvakosterstrommen prices: **excluding VAT** (`docs/DATA.md` §1). `fetch_prices.py` stores
  them exactly as received — no VAT math happens in ingestion.
- `invoice_reproduction.py` applies VAT once, at the end of the energy line, per
  `docs/DOMAIN.md` §5 (`0%` in NO4). Nettleie's VAT placement is verified against the real
  invoice rather than assumed — see the comments in that file.

## Things this spike already caught

- **`pandas==2.2.3` segfaults under Python 3.14** on `pd.to_datetime(..., utc=True)` when
  the input has mixed UTC offsets (e.g. `+02:00` and `+01:00` straddling a DST change) —
  exactly what a year of Norwegian timestamps looks like. No exception, no traceback, just
  `SIGSEGV`. Root cause: pandas 2.2.3 predates Python 3.14 support. Fixed by pinning
  `pandas==3.0.5` instead (see `requirements.txt`). Worth remembering for Phase 3's real
  `python/` toolchain if it also lands on a very new Python.

## Goal 4 status: no real invoice, upgraded to real Elhub Open Data instead

The user had no real invoice to share. Alternatives tried, in order:

1. **A public bill calculator (minspotpris.no)** as an independent oracle -- found one that
   does the right calculation (real NO2 spot prices + strømstøtte + nettleie), but its form
   is React-bound and silently ignored programmatic field overrides, so I couldn't feed it
   controlled inputs and trust the result as independent verification without a lot more
   effort reverse-engineering its frontend. Abandoned rather than sink more time into it.
2. **Real published rates + real spot prices + a synthetic consumption profile**
   (`invoice_fixture_run.py`) -- a documented archetype shape scaled to SSB's published
   ~20,000 kWh/year detached-house average. Result: 4,364 kr incl. VAT for the synthetic
   month. Plausible, but a guess is still a guess.
3. **Real published rates + real spot prices + real Elhub Open Data consumption**
   (`invoice_elhub_run.py`) -- what actually closes most of the gap. The user pulled
   Elhub's public Open Data exports (`data.elhub.no`, no login -- these are the
   *aggregated* datasets DATA.md §4 calls a legitimate cold-start population prior, not a
   personal per-meter export) into `spike/data/elhub_open/`.
   `consumptionPerGroupMbaHour` gives real, measured `QUANTITY_KWH` and
   `METERING_POINT_COUNT` per price area, consumption group and hour; dividing the two
   gives a **real, measured** average NO2 household's hourly kWh for January 2026 --
   744/744 hours matched against the real spot price series with no gaps. Result:
   **3,352 kr incl. VAT** for January 2026, average NO2 household, Glitre Nett rates.

**Two honest caveats on the Elhub-Open-Data result, not glossed over:**
- It's the **NO2-wide average household**, not specifically a Glitre-Nett/Kristiansand one
  -- NO2 also covers Stavanger (a different grid company, Lnett). A tighter cut exists
  (`consumptionPerGroupMunicipalityHour`, filterable to Kristiansand's municipality code
  4204) but wasn't processed -- it's a 359 MB zip and wasn't needed to get the energy and
  energiledd lines onto real ground.
- **Kapasitetsledd is still not trustworthy from this data.** Averaging over ~632,000
  metering points smooths out exactly the individual peaks (one shower, cooking, a fridge
  cycling) that the top-3-døgnmaks rule is about. A real single home's load is far peakier
  than a population mean, so the 186 kr kapasitetsledd figure is an understatement of what
  any actual household would see. The energy-line and energiledd totals don't have this
  problem -- they scale linearly with total kWh, so averaging doesn't bias them.

**Bottom line: this still doesn't satisfy ROADMAP.md's Phase 0 done-criterion**
("reproduced a real invoice to within a krone") for the bill as a whole, though the energy
and energiledd lines are now grounded in real measured data rather than a guess. Closing
the remaining gap -- specifically for kapasitetsledd -- needs one real meter's actual
hourly consumption, e.g. the user's own Elhub/grid-company "Min side" export.

The other Elhub files the user provided (`exchangePerMba*`, `productionPerGroupMbaHour`,
`productionPerMba15min`, `lossPerMbaHour`, `completenessDailyPerMba`,
`norgesprisConsumptionPerGroupEacMba`) weren't needed for this bill-reproduction goal, but
are worth keeping in mind for Phase 3 -- `productionPerGroupMbaHour` and `exchangePerMbaHour`
are exactly the kind of domestic cross-market features FORECASTING.md §4 describes
(normally sourced from ENTSO-E; Elhub gives a Norway-specific version for free, no API key).

## Known scope-cuts in this spike (deliberate, not oversights)

- `invoice_reproduction.py` hard-codes 2026 scheme parameters (strømstøtte threshold,
  Norgespris rate, VAT rate) instead of reading them from a versioned `scheme_parameter`
  table. `CLAUDE.md` rule 3 forbids this in the real app; it's fine here because nothing
  ships from this folder.
- The notebook's train/test split is a simple holdout, **not** the walk-forward,
  issue-time-anchored, embargoed protocol in `docs/FORECASTING.md` §6. That harness is
  Phase 3 work.
- No point-in-time correctness guarantees on the weather side — one live fetch, no historical
  archive of past forecast runs.

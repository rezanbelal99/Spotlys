"""Phase 0 spike, goal 1 (docs/ROADMAP.md).

Pull 12 months of NO2 day-ahead prices from hvakosterstrommen.no into a CSV, confirming:
  - the VAT convention (docs/DATA.md §1: prices are EXCLUDING VAT)
  - the EXR field (the EUR->NOK rate hvakosterstrommen used for that day)

Throwaway script. Nothing here ships (see spike/README.md).

Endpoint and schema, confirmed live on 2026-09-15:
  GET https://www.hvakosterstrommen.no/api/v1/prices/{YYYY}/{MM}-{DD}_{NOx}.json
  -> [{"NOK_per_kWh": 0.7532, "EUR_per_kWh": 0.06369, "EXR": 11.826,
       "time_start": "2026-01-01T00:00:00+01:00", "time_end": "...+01:00"}, ...]

Note NOK_per_kWh is kroner per kWh, not øre. docs/DOMAIN.md stores prices in øre/kWh, so
this script multiplies by 100 on the way into the CSV -- the API's own field is left
unconverted in the raw cache so the cache is a faithful copy of what the source actually said.
"""

from __future__ import annotations

import csv
import json
import sys
import time
from datetime import date, timedelta
from pathlib import Path

import requests

ZONE = "NO2"
DAYS_BACK = 365
REQUESTS_PER_SECOND = 2.0
BASE_URL = "https://www.hvakosterstrommen.no/api/v1/prices"

SPIKE_DIR = Path(__file__).resolve().parent
CACHE_DIR = SPIKE_DIR / "cache" / "hvakosterstrommen"
OUT_DIR = SPIKE_DIR / "out"
OUT_CSV = OUT_DIR / "no2_prices_12mo.csv"

# hvakosterstrommen.no asks for attribution -- see docs/DATA.md §1. Real wiring into the UI
# footer is Phase 1; this print is just so the obligation isn't forgotten in the spike.
ATTRIBUTION_NOTE = "Data via hvakosterstrommen.no -- attribution required in any shipped UI."


def cache_path(day: date, zone: str) -> Path:
    return CACHE_DIR / f"{day.isoformat()}_{zone}.json"


def fetch_day(day: date, zone: str, min_interval: float) -> list[dict] | None:
    """Return the day's raw records, from cache if present, else from the network.

    Returns None if the upstream has no data for that day (e.g. a date before the
    2022-09-01 archive start, or a transient 404) -- treated as an honest gap, not padded.
    """
    path = cache_path(day, zone)
    if path.exists():
        return json.loads(path.read_text())

    url = f"{BASE_URL}/{day.year}/{day.month:02d}-{day.day:02d}_{zone}.json"
    resp = requests.get(url, timeout=15)
    time.sleep(min_interval)  # rate limit every network call, hit or miss

    if resp.status_code == 404:
        return None
    resp.raise_for_status()

    records = resp.json()
    path.write_text(json.dumps(records))
    return records


def main() -> None:
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    min_interval = 1.0 / REQUESTS_PER_SECOND
    today = date.today()
    days = [today - timedelta(days=n) for n in range(DAYS_BACK, 0, -1)]

    rows: list[dict] = []
    missing_days: list[str] = []
    exr_missing = 0
    fetched_from_network = 0
    fetched_from_cache = 0

    for day in days:
        was_cached = cache_path(day, ZONE).exists()
        records = fetch_day(day, ZONE, min_interval)
        if was_cached:
            fetched_from_cache += 1
        else:
            fetched_from_network += 1

        if records is None:
            missing_days.append(day.isoformat())
            continue

        for rec in records:
            if "EXR" not in rec:
                exr_missing += 1
            rows.append(
                {
                    "zone": ZONE,
                    "hour_start": rec["time_start"],
                    "hour_end": rec["time_end"],
                    "price_ex_vat_ore_per_kwh": round(rec["NOK_per_kWh"] * 100, 4),
                    "eur_per_kwh": rec["EUR_per_kWh"],
                    "exr": rec.get("EXR"),
                    "source": "hkso",
                }
            )

    with OUT_CSV.open("w", newline="") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "zone",
                "hour_start",
                "hour_end",
                "price_ex_vat_ore_per_kwh",
                "eur_per_kwh",
                "exr",
                "source",
            ],
        )
        writer.writeheader()
        writer.writerows(rows)

    prices = [r["price_ex_vat_ore_per_kwh"] for r in rows]
    print(f"Zone: {ZONE}")
    print(f"Requested {len(days)} days, {fetched_from_cache} from cache, {fetched_from_network} fetched live")
    print(f"Rows written: {len(rows)} -> {OUT_CSV}")
    print(f"Missing days (no upstream data): {len(missing_days)}")
    if missing_days:
        print(f"  first few: {missing_days[:5]}")
    print(f"Rows missing EXR field: {exr_missing} (should be 0 -- confirms the EXR convention)")
    if prices:
        print(f"price_ex_vat_ore_per_kwh: min={min(prices):.2f} max={max(prices):.2f}")
    print(ATTRIBUTION_NOTE)


if __name__ == "__main__":
    sys.exit(main())

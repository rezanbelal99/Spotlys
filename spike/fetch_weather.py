"""Phase 0 spike, goal 2 (docs/ROADMAP.md).

Fetch a MET Locationforecast 2.0 forecast for one representative NO2 point, with a
compliant User-Agent, honouring cache headers so a second run inside the cache window
makes zero network calls (docs/DATA.md §2 -- generic User-Agents get blocked, and ignoring
cache headers gets you banned).

Throwaway script. Nothing here ships (see spike/README.md).

Point chosen: Kristiansand, NO2's largest city (docs/DOMAIN.md §1). Lat/lon rounded to 4
decimals, altitude to the nearest metre, per docs/DATA.md §2.
"""

from __future__ import annotations

import json
import sys
from email.utils import format_datetime, parsedate_to_datetime
from datetime import datetime, timezone
from pathlib import Path

import requests

LAT = 58.1467
LON = 7.9956
ALTITUDE_M = 15

# Confirmed with the user: their real email, as MET's own docs ask for a real contact.
USER_AGENT = "spotlys-spike/0.1 (rezanbeirut@gmail.com)"

URL = "https://api.met.no/weatherapi/locationforecast/2.0/complete"

SPIKE_DIR = Path(__file__).resolve().parent
CACHE_DIR = SPIKE_DIR / "cache" / "met"
OUT_DIR = SPIKE_DIR / "out"
OUT_JSON = OUT_DIR / "met_forecast_sample.json"

CACHE_BODY = CACHE_DIR / "kristiansand.json"
CACHE_META = CACHE_DIR / "kristiansand.meta.json"


def load_cache_meta() -> dict | None:
    if CACHE_META.exists():
        return json.loads(CACHE_META.read_text())
    return None


def still_fresh(meta: dict) -> bool:
    expires = meta.get("expires")
    if not expires:
        return False
    return parsedate_to_datetime(expires) > datetime.now(timezone.utc)


def fetch() -> tuple[dict, dict]:
    """Return (payload, meta). Uses the on-disk cache if still within `Expires`."""
    meta = load_cache_meta()
    if meta and still_fresh(meta) and CACHE_BODY.exists():
        return json.loads(CACHE_BODY.read_text()), {**meta, "source": "cache (fresh)"}

    headers = {"User-Agent": USER_AGENT}
    if meta and meta.get("last_modified"):
        headers["If-Modified-Since"] = meta["last_modified"]

    resp = requests.get(
        URL,
        params={"lat": LAT, "lon": LON, "altitude": ALTITUDE_M},
        headers=headers,
        timeout=15,
    )

    if resp.status_code == 304 and CACHE_BODY.exists():
        # Not modified: keep the cached body, refresh the expiry we were given.
        new_meta = {
            "last_modified": resp.headers.get("Last-Modified", meta.get("last_modified")),
            "expires": resp.headers.get("Expires"),
            "fetched_at": format_datetime(datetime.now(timezone.utc)),
        }
        CACHE_META.write_text(json.dumps(new_meta))
        return json.loads(CACHE_BODY.read_text()), {**new_meta, "source": "network (304 not modified)"}

    resp.raise_for_status()
    payload = resp.json()

    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_BODY.write_text(json.dumps(payload))
    new_meta = {
        "last_modified": resp.headers.get("Last-Modified"),
        "expires": resp.headers.get("Expires"),
        "fetched_at": format_datetime(datetime.now(timezone.utc)),
    }
    CACHE_META.write_text(json.dumps(new_meta))
    return payload, {**new_meta, "source": "network (200)"}


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    payload, meta = fetch()

    OUT_JSON.write_text(json.dumps(payload, indent=2))

    print(f"Point: Kristiansand ({LAT}, {LON}, {ALTITUDE_M} m)")
    print(f"Source of this response: {meta['source']}")
    print(f"Last-Modified: {meta.get('last_modified')}")
    print(f"Expires:       {meta.get('expires')}")
    print(f"Saved full payload -> {OUT_JSON}")

    timeseries = payload["properties"]["timeseries"][:48]
    print(f"\nNext {len(timeseries)} hourly entries (time, temp_c, wind_ms, cloud_frac, precip_mm next 1h):")
    for entry in timeseries[:6]:
        t = entry["time"]
        details = entry["data"]["instant"]["details"]
        temp = details.get("air_temperature")
        wind = details.get("wind_speed")
        cloud = details.get("cloud_area_fraction")
        precip = (
            entry["data"].get("next_1_hours", {}).get("details", {}).get("precipitation_amount")
        )
        print(f"  {t}  temp={temp}  wind={wind}  cloud={cloud}  precip_1h={precip}")
    print("  ...")


if __name__ == "__main__":
    sys.exit(main())

"""Phase 0 spike, goal 4 -- real Elhub Open Data consumption, replacing the guessed shape.

The user pulled Elhub's public Open Data exports (no login needed -- these are the
aggregated population datasets DATA.md §4 calls "Elhub open aggregated datasets: useful as
a population prior for cold-start consumption profiles, not per-user", not a personal
per-meter export). `consumptionPerGroupMbaHour` gives real, measured, hourly
QUANTITY_KWH and METERING_POINT_COUNT per price area and consumption group -- dividing the
two gives a real average kWh/hour for an NO2 household, for real, for January 2026. This
replaces invoice_fixture_run.py's made-up archetype shape with actually-measured data.

What's REAL now:
  - NO2 spot prices, January 2026 -- spike/out/no2_prices_12mo.csv (goal 1)
  - Glitre Nett rates, effective 2026-07-01 -- fetched live from their price page
  - Consumption shape -- Elhub Open Data, consumptionPerGroupMbaHour, NO2, group=household,
    January 2026, QUANTITY_KWH / METERING_POINT_COUNT per hour. Real measured population
    data, not guessed.

What's STILL NOT a real invoice, said plainly:
  - This is the NO2-WIDE average household, not a Glitre-Nett-specific (Kristiansand-area)
    one -- NO2 also covers Stavanger, served by a different grid company (Lnett). A tighter
    cut exists (consumptionPerGroupMunicipalityHour, filterable to Kristiansand's
    municipality code 4204) but wasn't processed here; it's a 359 MB zip and this file
    already gets the energy-line and energiledd numbers onto real ground. Worth doing if
    more precision is wanted.
  - Averaging over ~632,000 metering points smooths out exactly the peaks that
    kapasitetsledd is about. A real individual home's hourly load is much peakier (fridge
    cycles, one shower, cooking) than a population mean. So while the energy and energiledd
    lines below are now grounded in real measured consumption, the kapasitetsledd figure is
    understated relative to what any actual single household would see -- flagged in the
    output, not hidden.
"""

from __future__ import annotations

import csv
from datetime import datetime
from pathlib import Path

from invoice_fixture_run import FORBRUKSAVGIFT_EX_VAT_ORE, ENOVA_EX_VAT_ORE_PER_KWH_EQUIV, GLITRE_NETT
from invoice_reproduction import HourlyRecord, HouseholdProfile, SupportScheme, Zone, calculate_bill

SPIKE_DIR = Path(__file__).resolve().parent
ELHUB_CSV = (
    SPIKE_DIR
    / "data"
    / "elhub_open"
    / "consumptionPerGroupMbaHour"
    / "part-00000-949ece0e-247f-4338-be69-09a81edbdb6d-c000.csv"
)


def load_elhub_avg_household_kwh(zone: str, year: int, month: int) -> dict[datetime, float]:
    """Real Elhub Open Data: average kWh per household metering point, per hour."""
    out: dict[datetime, float] = {}
    with ELHUB_CSV.open() as f:
        for row in csv.DictReader(f):
            if row["PRICE_AREA"] != zone or row["CONSUMPTION_GROUP"] != "household":
                continue
            t = datetime.fromisoformat(row["START_TIME"])
            if t.year != year or t.month != month:
                continue
            out[t] = float(row["QUANTITY_KWH"]) / int(row["METERING_POINT_COUNT"])
    return out


def load_january_2026_prices() -> dict[datetime, float]:
    out = {}
    with (SPIKE_DIR / "out" / "no2_prices_12mo.csv").open() as f:
        for row in csv.DictReader(f):
            t = datetime.fromisoformat(row["hour_start"])
            if t.year == 2026 and t.month == 1:
                out[t] = float(row["price_ex_vat_ore_per_kwh"])
    return out


def main() -> None:
    consumption = load_elhub_avg_household_kwh("NO2", 2026, 1)
    prices = load_january_2026_prices()

    common_hours = sorted(set(consumption) & set(prices))
    print(f"Consumption rows: {len(consumption)}, price rows: {len(prices)}, matched: {len(common_hours)}")
    if len(common_hours) != 744:
        print("WARNING: expected 744 hours (31 days x 24h) for January 2026")

    # Align both series to the same instant. The Elhub file's timestamps carry a +01:00
    # offset (Oslo standard time) even for January, which has no DST -- consistent, no
    # conversion issue expected, but confirmed here rather than assumed.
    hourly = [
        HourlyRecord(
            hour_start=t,
            consumption_kwh=consumption[t],
            spot_price_ex_vat_ore_per_kwh=prices[t],
        )
        for t in common_hours
    ]

    profile = HouseholdProfile(
        zone=Zone.NO2,
        grid_tariff=GLITRE_NETT,
        scheme=SupportScheme.STROMSTOTTE,
        forbruksavgift_ex_vat_ore_per_kwh=FORBRUKSAVGIFT_EX_VAT_ORE + ENOVA_EX_VAT_ORE_PER_KWH_EQUIV,
        enova_annual_ex_vat_nok=0.0,
        supplier_monthly_fee_ex_vat_nok=0.0,
    )

    total_kwh = sum(r.consumption_kwh for r in hourly)
    max_hour_kwh = max(r.consumption_kwh for r in hourly)
    print("=" * 66)
    print("REAL consumption shape (Elhub Open Data, NO2 household average) +")
    print("REAL NO2 spot prices (Jan 2026) + REAL Glitre Nett rates (2026-07-01).")
    print("Still an NO2-wide average, not one specific meter -- see module docstring.")
    print("=" * 66)
    print(f"Month: January 2026   Avg-household consumption: {total_kwh:.0f} kWh")
    print(f"Max single hour: {max_hour_kwh:.2f} kWh (kapasitetsledd input -- population-smoothed, see caveat)")
    print()

    bill = calculate_bill(hourly, profile)
    bill.print_report()


if __name__ == "__main__":
    main()

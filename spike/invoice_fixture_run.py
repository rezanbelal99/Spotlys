"""Phase 0 spike, goal 4 -- run with the best available alternative to a real invoice.

The user has no real invoice to test against (asked, they don't have one) and no real
metered consumption either. This script documents exactly what's real and what's not in
the numbers it produces, per CLAUDE.md's "never invent a number" rule -- this is NOT a
verified reproduction, and ROADMAP.md's Phase 0 done-criterion ("reproduced a real invoice
to within a krone") is honestly NOT met by this run. See spike/README.md for what would
close that gap.

What's REAL, sourced, and independently fetched:
  - NO2 day-ahead spot prices for January 2026 -- from spike/out/no2_prices_12mo.csv,
    fetched live from hvakosterstrommen.no in goal 1, not synthetic.
  - Grid tariff rates -- Glitre Nett (the actual grid company for the Kristiansand/Agder
    area, NO2), private-customer rates effective 2026-07-01, fetched live from
    https://www.glitrenett.no/kunde/nettleie-og-priser/nettleiepriser-privatkunde on
    2026-09-16. Kapasitetsledd table is their full published 10-tier schedule.
  - Forbruksavgift and Enova rates -- same source, and cross-checked: they match the
    figures independently published on L-nett's (Sør-Rogaland, also NO2) price page, which
    makes sense since forbruksavgift is a national rate set by the Storting, not
    company-specific.
  - strømstøtte threshold (77 øre ex VAT, 2026) and Norwegian public holidays (via the
    `holidays` package) -- as in invoice_reproduction.py.

What's SYNTHETIC and clearly not verified:
  - The household's hourly consumption. No real household to draw from, so this uses a
    documented archetype shape (see build_synthetic_profile below) scaled to match SSB's
    published 2025 average for a detached house (~20 000 kWh/year;
    https://www.ssb.no/energi-og-industri/energi/artikler/hva-er-gjennomsnittlig-stromforbruk-i-husholdningene),
    NOT a real meter reading. This is exactly the DATA.md §4 "cold start" approach
    (archetype x heating-degree-day proxy), used here for lack of anything better.

Because the consumption is synthetic, there is no real invoice total to compare the output
against -- this run demonstrates the domain formulas execute correctly against real rates
and real prices, nothing more. A tried alternative -- cross-checking against a third-party
Norwegian bill calculator (minspotpris.no) -- didn't pan out: its form is React-bound and
silently ignored programmatic field overrides, so I couldn't trust it as an independent
oracle without a lot more effort reverse-engineering its frontend, and I cut that short
rather than keep burning time on it.
"""

from __future__ import annotations

import csv
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

from invoice_reproduction import (
    GridTariff,
    HourlyRecord,
    HouseholdProfile,
    SupportScheme,
    Zone,
    calculate_bill,
)

OSLO = ZoneInfo("Europe/Oslo")
SPIKE_DIR = Path(__file__).resolve().parent

# Glitre Nett, private customer, effective 2026-07-01. Ex-VAT = incl-VAT / 1.25.
# Source: https://www.glitrenett.no/kunde/nettleie-og-priser/nettleiepriser-privatkunde
GLITRE_NETT = GridTariff(
    grid_company_name="Glitre Nett (Agder/Kristiansand, effective 2026-07-01)",
    energy_day_ex_vat_ore=42.16 / 1.25,
    energy_night_ex_vat_ore=27.16 / 1.25,
    capacity_steps=[
        (0, 2, 160.00 / 1.25),
        (2, 5, 232.50 / 1.25),
        (5, 10, 390.00 / 1.25),
        (10, 15, 730.00 / 1.25),
        (15, 20, 965.00 / 1.25),
        (20, 25, 1210.00 / 1.25),
        (25, 50, 1885.00 / 1.25),
        (50, 75, 2990.00 / 1.25),
        (75, 100, 3990.00 / 1.25),
        (100, float("inf"), 6665.00 / 1.25),
    ],
)

FORBRUKSAVGIFT_EX_VAT_ORE = 8.91 / 1.25  # national rate, same source
ENOVA_EX_VAT_ORE_PER_KWH_EQUIV = 1.25 / 1.25  # shown per-kWh by the source; see note below


def build_synthetic_profile(hour_starts: list[datetime], annual_kwh_target: float) -> list[float]:
    """A documented archetype shape, NOT a real meter reading.

    Diurnal shape: low overnight baseline, a modest morning bump, a larger evening bump
    (cooking, heating recovery, lighting) -- the standard Norwegian household diurnal
    pattern described qualitatively in docs/DATA.md §4's "cold start" approach. Winter
    months are scaled up relative to summer using a fixed monthly multiplier standing in
    for heating-degree-days (no weather integration in this spike -- that's Phase 3).
    Weekends get a flatter, slightly higher daytime profile (people home).

    Scaled so the sum over a full synthetic year would equal `annual_kwh_target` -- SSB's
    published ~20 000 kWh/year average for a Norwegian detached house.
    """
    base_shape = {
        0: 0.5, 1: 0.45, 2: 0.4, 3: 0.4, 4: 0.4, 5: 0.5,
        6: 0.9, 7: 1.4, 8: 1.2, 9: 0.8, 10: 0.7, 11: 0.7,
        12: 0.7, 13: 0.7, 14: 0.75, 15: 0.85, 16: 1.1, 17: 1.6,
        18: 1.9, 19: 1.7, 20: 1.5, 21: 1.1, 22: 0.8, 23: 0.6,
    }
    month_multiplier = {
        1: 1.55, 2: 1.45, 3: 1.25, 4: 1.0, 5: 0.75, 6: 0.55,
        7: 0.5, 8: 0.55, 9: 0.7, 10: 0.95, 11: 1.25, 12: 1.5,
    }
    raw_annual_sum = sum(base_shape.values()) * 365.25 * (sum(month_multiplier.values()) / 12)
    scale = annual_kwh_target / raw_annual_sum

    values = []
    for t in hour_starts:
        local = t.astimezone(OSLO)
        weekend_bump = 1.15 if local.weekday() >= 5 and 8 <= local.hour <= 18 else 1.0
        values.append(base_shape[local.hour] * month_multiplier[local.month] * weekend_bump * scale)
    return values


def load_january_2026_prices() -> list[HourlyRecord]:
    records = []
    with (SPIKE_DIR / "out" / "no2_prices_12mo.csv").open() as f:
        for row in csv.DictReader(f):
            t = datetime.fromisoformat(row["hour_start"])
            if t.year == 2026 and t.month == 1:
                records.append((t, float(row["price_ex_vat_ore_per_kwh"])))
    records.sort(key=lambda r: r[0])
    return records


def main() -> None:
    price_rows = load_january_2026_prices()
    if len(price_rows) != 31 * 24:
        print(f"WARNING: expected 744 hourly rows for January 2026, got {len(price_rows)}")

    hours = [t for t, _ in price_rows]
    consumption = build_synthetic_profile(hours, annual_kwh_target=20_000)

    hourly = [
        HourlyRecord(hour_start=t, consumption_kwh=kwh, spot_price_ex_vat_ore_per_kwh=price)
        for (t, price), kwh in zip(price_rows, consumption)
    ]

    # Glitre publishes Enova as an øre/kWh addend, not an annual fixed fee like
    # HouseholdProfile.enova_annual_ex_vat_nok assumes -- folded into the per-kWh levy line
    # instead, since both are flat national øre/kWh levies collected the same way.
    profile = HouseholdProfile(
        zone=Zone.NO2,
        grid_tariff=GLITRE_NETT,
        scheme=SupportScheme.STROMSTOTTE,
        forbruksavgift_ex_vat_ore_per_kwh=FORBRUKSAVGIFT_EX_VAT_ORE + ENOVA_EX_VAT_ORE_PER_KWH_EQUIV,
        enova_annual_ex_vat_nok=0.0,
        supplier_monthly_fee_ex_vat_nok=0.0,  # no supplier chosen -- grid-only bill
    )

    total_kwh = sum(consumption)
    print("=" * 60)
    print("SYNTHETIC profile -- NOT a real invoice, NOT verified against ground truth.")
    print("Real: NO2 spot prices (Jan 2026), Glitre Nett published rates (2026-07-01).")
    print("Synthetic: hourly consumption, scaled to SSB's ~20,000 kWh/year archetype.")
    print("=" * 60)
    print(f"Zone: {profile.zone.value}   Grid company: {profile.grid_tariff.grid_company_name}")
    print(f"Month: January 2026   Synthetic consumption this month: {total_kwh:.0f} kWh")
    print()

    bill = calculate_bill(hourly, profile)
    bill.print_report()  # no invoice_total_inc_vat_nok -- nothing to compare against


if __name__ == "__main__":
    main()

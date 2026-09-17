"""Phase 0 spike, goal 4 (docs/ROADMAP.md).

Reproduce one real Norwegian electricity invoice by hand, implementing the bill formula
from docs/DOMAIN.md §5 as plain functions. This is the domain-understanding check: if this
can't be made to match a real invoice to within a krone, nothing downstream can be trusted
(ROADMAP.md's Phase 0 done-criterion).

Throwaway script. Nothing here ships (see spike/README.md). In particular:
  - Scheme parameters (strømstøtte threshold, Norgespris rate, VAT) are hard-coded below.
    CLAUDE.md rule 3 forbids this in the real app -- they belong in the versioned
    `scheme_parameter` table with a source URL. Fine here; not fine in Spotlys.Domain.
  - No database, no EF Core, no TimeProvider -- this is pen-and-paper arithmetic in Python.

STATUS: skeleton only. The domain rules are wired up; the fixture data at the bottom is
NOT filled in yet -- it needs a real invoice, which I'm asking for separately. Do not treat
the placeholder numbers in `if __name__ == "__main__"` as real: they're structurally valid
but not verified against anything.

VAT convention: every intermediate function below operates on ex-VAT amounts (matching how
docs/DOMAIN.md says the domain stores prices). VAT is applied exactly once, in
`calculate_bill`, to the whole bill total, at the zone's VAT rate (0% in NO4) -- per
DOMAIN.md §5's `± VAT by zone` on the *complete* bill, not per line. This is my working
assumption, not a confirmed fact: real grid-company invoices sometimes show nettleie
already VAT-inclusive on their own line. Whether that matches this invoice's actual layout
is exactly the thing this script is meant to check once real numbers are in.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import date, datetime, timedelta
from enum import Enum
from zoneinfo import ZoneInfo

import holidays

OSLO = ZoneInfo("Europe/Oslo")

# ---------------------------------------------------------------------------
# Scheme parameters -- docs/DOMAIN.md §3. 2026 values, hard-coded ONLY because this
# script never ships. Source: docs/DOMAIN.md itself, which cites the 2026 national budget.
# ---------------------------------------------------------------------------

STROMSTOTTE_THRESHOLD_EX_VAT_ORE = 77.0  # øre/kWh, 2026
STROMSTOTTE_SUPPORT_RATE = 0.90
NORGESPRIS_RATE_EX_VAT_ORE = 40.0  # øre/kWh, 2026 (50 øre incl. VAT)
NORGESPRIS_CAP_KWH_HOME = 5000
NORGESPRIS_CAP_KWH_CABIN = 1000
VAT_RATE_STANDARD = 0.25
VAT_RATE_NO4 = 0.0  # docs/DOMAIN.md §1: Nordland, Troms, Finnmark pay no VAT on electricity


class Zone(str, Enum):
    NO1 = "NO1"
    NO2 = "NO2"
    NO3 = "NO3"
    NO4 = "NO4"
    NO5 = "NO5"


def vat_rate_for_zone(zone: Zone) -> float:
    return VAT_RATE_NO4 if zone == Zone.NO4 else VAT_RATE_STANDARD


class SupportScheme(str, Enum):
    STROMSTOTTE = "stromstotte"
    NORGESPRIS = "norgespris"


@dataclass(frozen=True)
class GridTariff:
    """One grid company's published rates. Needed from the invoice / the grid company's
    own price list -- not something this spike can guess. Filled in once we have both.
    """

    grid_company_name: str
    energy_day_ex_vat_ore: float  # per kWh, 06:00-22:00 weekdays
    energy_night_ex_vat_ore: float  # per kWh, nights + weekends + holidays
    capacity_steps: list[tuple[float, float, float]]  # (from_kw, to_kw, monthly_nok_ex_vat)


@dataclass(frozen=True)
class HouseholdProfile:
    zone: Zone
    grid_tariff: GridTariff
    scheme: SupportScheme
    is_cabin: bool = False
    supplier_markup_ex_vat_ore: float = 0.0  # påslag
    supplier_monthly_fee_ex_vat_nok: float = 0.0
    forbruksavgift_ex_vat_ore_per_kwh: float = 0.0  # time-versioned in the real app; a
    #   single figure here because this is one month's invoice, not a multi-year series.
    enova_annual_ex_vat_nok: float = 0.0


@dataclass(frozen=True)
class HourlyRecord:
    hour_start: datetime  # tz-aware, Europe/Oslo
    consumption_kwh: float
    spot_price_ex_vat_ore_per_kwh: float


_NO_HOLIDAYS_CACHE: dict[int, holidays.HolidayBase] = {}


def _is_norwegian_holiday(d: date) -> bool:
    if d.year not in _NO_HOLIDAYS_CACHE:
        _NO_HOLIDAYS_CACHE[d.year] = holidays.Norway(years=d.year)
    return d in _NO_HOLIDAYS_CACHE[d.year]


def is_nettleie_night(hour_start: datetime) -> bool:
    """docs/DOMAIN.md §4b: weekday nights 22:00-06:00, all Saturdays/Sundays, and public
    holidays get the cheaper energiledd rate."""
    local = hour_start.astimezone(OSLO)
    if local.weekday() >= 5:  # Saturday=5, Sunday=6
        return True
    if _is_norwegian_holiday(local.date()):
        return True
    return local.hour >= 22 or local.hour < 6


def stromstotte_ore_per_kwh(spot_ex_vat_ore: float) -> float:
    """docs/DOMAIN.md §3a. Hourly, 90% of the amount above the threshold."""
    return STROMSTOTTE_SUPPORT_RATE * max(0.0, spot_ex_vat_ore - STROMSTOTTE_THRESHOLD_EX_VAT_ORE)


def energy_cost_ex_vat_ore(
    record: HourlyRecord,
    profile: HouseholdProfile,
    month_to_date_kwh_before_this_hour: float,
) -> float:
    """docs/DOMAIN.md §3a/§3b. Returns ex-VAT øre for this single hour's energy line
    (spot ± regime, including påslag), NOT multiplied by consumption yet -- see below."""
    if profile.scheme == SupportScheme.NORGESPRIS:
        cap = NORGESPRIS_CAP_KWH_CABIN if profile.is_cabin else NORGESPRIS_CAP_KWH_HOME
        if month_to_date_kwh_before_this_hour + record.consumption_kwh <= cap:
            return NORGESPRIS_RATE_EX_VAT_ORE  # spot-independent, per kWh
        # Above the cap: ordinary market agreement, no support at all (DOMAIN.md §3b)
        return record.spot_price_ex_vat_ore_per_kwh + profile.supplier_markup_ex_vat_ore

    # strømstøtte (default)
    stotte = stromstotte_ore_per_kwh(record.spot_price_ex_vat_ore_per_kwh)
    return record.spot_price_ex_vat_ore_per_kwh + profile.supplier_markup_ex_vat_ore - stotte


def top3_dognmaks_kw(hourly: list[HourlyRecord]) -> float:
    """docs/DOMAIN.md §4a: average of the three highest hourly consumption values, taken
    from three DIFFERENT days. Hourly kWh == kW for a one-hour period."""
    best_per_day: dict[date, float] = {}
    for rec in hourly:
        d = rec.hour_start.astimezone(OSLO).date()
        best_per_day[d] = max(best_per_day.get(d, 0.0), rec.consumption_kwh)

    top3 = sorted(best_per_day.values(), reverse=True)[:3]
    if not top3:
        return 0.0
    return sum(top3) / len(top3)


def kapasitetsledd_ex_vat_nok(peak_kw: float, steps: list[tuple[float, float, float]]) -> float:
    for from_kw, to_kw, monthly_nok in steps:
        if from_kw <= peak_kw < to_kw:
            return monthly_nok
    # above the highest defined step: use the last step's rate rather than silently 0
    return steps[-1][2] if steps else 0.0


@dataclass
class BillBreakdown:
    energy_ex_vat_nok: float = 0.0
    energiledd_ex_vat_nok: float = 0.0
    kapasitetsledd_ex_vat_nok: float = 0.0
    forbruksavgift_ex_vat_nok: float = 0.0
    enova_ex_vat_nok: float = 0.0
    supplier_fee_ex_vat_nok: float = 0.0
    vat_rate: float = 0.0
    total_ex_vat_nok: float = field(init=False, default=0.0)
    total_inc_vat_nok: float = field(init=False, default=0.0)

    def finalize(self) -> "BillBreakdown":
        self.total_ex_vat_nok = (
            self.energy_ex_vat_nok
            + self.energiledd_ex_vat_nok
            + self.kapasitetsledd_ex_vat_nok
            + self.forbruksavgift_ex_vat_nok
            + self.enova_ex_vat_nok
            + self.supplier_fee_ex_vat_nok
        )
        self.total_inc_vat_nok = self.total_ex_vat_nok * (1 + self.vat_rate)
        return self

    def print_report(self, invoice_total_inc_vat_nok: float | None = None) -> None:
        print(f"{'line':<28} {'ex-VAT kr':>12}")
        print(f"{'energy (spot ± regime)':<28} {self.energy_ex_vat_nok:>12.2f}")
        print(f"{'energiledd (day/night)':<28} {self.energiledd_ex_vat_nok:>12.2f}")
        print(f"{'kapasitetsledd':<28} {self.kapasitetsledd_ex_vat_nok:>12.2f}")
        print(f"{'forbruksavgift':<28} {self.forbruksavgift_ex_vat_nok:>12.2f}")
        print(f"{'enova':<28} {self.enova_ex_vat_nok:>12.2f}")
        print(f"{'supplier monthly fee':<28} {self.supplier_fee_ex_vat_nok:>12.2f}")
        print(f"{'-' * 42}")
        print(f"{'total ex-VAT':<28} {self.total_ex_vat_nok:>12.2f}")
        print(f"{'VAT @ ' + f'{self.vat_rate:.0%}':<28} {self.total_inc_vat_nok - self.total_ex_vat_nok:>12.2f}")
        print(f"{'total incl. VAT':<28} {self.total_inc_vat_nok:>12.2f}")
        if invoice_total_inc_vat_nok is not None:
            delta = self.total_inc_vat_nok - invoice_total_inc_vat_nok
            print(f"{'-' * 42}")
            print(f"{'real invoice total':<28} {invoice_total_inc_vat_nok:>12.2f}")
            print(f"{'delta':<28} {delta:>+12.2f}")
            verdict = "MATCH (within 1 kr)" if abs(delta) <= 1.0 else "MISMATCH"
            print(f"\n{verdict}")


def calculate_bill(hourly: list[HourlyRecord], profile: HouseholdProfile) -> BillBreakdown:
    hourly_sorted = sorted(hourly, key=lambda r: r.hour_start)

    bill = BillBreakdown(vat_rate=vat_rate_for_zone(profile.zone))
    month_to_date_kwh = 0.0

    for rec in hourly_sorted:
        energy_ore_per_kwh = energy_cost_ex_vat_ore(rec, profile, month_to_date_kwh)
        bill.energy_ex_vat_nok += energy_ore_per_kwh * rec.consumption_kwh / 100.0

        energiledd_ore = (
            profile.grid_tariff.energy_night_ex_vat_ore
            if is_nettleie_night(rec.hour_start)
            else profile.grid_tariff.energy_day_ex_vat_ore
        )
        bill.energiledd_ex_vat_nok += energiledd_ore * rec.consumption_kwh / 100.0

        bill.forbruksavgift_ex_vat_nok += (
            profile.forbruksavgift_ex_vat_ore_per_kwh * rec.consumption_kwh / 100.0
        )

        month_to_date_kwh += rec.consumption_kwh

    peak_kw = top3_dognmaks_kw(hourly_sorted)
    bill.kapasitetsledd_ex_vat_nok = kapasitetsledd_ex_vat_nok(
        peak_kw, profile.grid_tariff.capacity_steps
    )
    bill.enova_ex_vat_nok = profile.enova_annual_ex_vat_nok / 12
    bill.supplier_fee_ex_vat_nok = profile.supplier_monthly_fee_ex_vat_nok

    return bill.finalize()


if __name__ == "__main__":
    print(
        "This is the skeleton only -- no real invoice loaded yet.\n"
        "Needed to fill in the fixture below and actually run this:\n"
        "  1. The invoice itself (a month, a total, ideally a line-item breakdown)\n"
        "  2. Grid company name + its published day/night energiledd rates and capacity"
        " step table for that month\n"
        "  3. Zone (NO1-NO5) and scheme (stromstotte or norgespris)\n"
        "  4. Hourly (or at least daily-peak) consumption for the month -- needed for"
        " kapasitetsledd's top-3 dognmaks rule. A monthly total alone isn't enough for that"
        " one line; if that's all that's available, the capacity step as already stated on"
        " the invoice can be used directly and kapasitetsledd's derivation left unverified.\n"
        "\nAsking for this rather than guessing at Norwegian rates, per CLAUDE.md's working"
        " agreement."
    )

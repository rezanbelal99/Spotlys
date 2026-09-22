import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchDemoBill, fetchStatus } from '../api/client'
import { Ribbon } from '../components/Ribbon'
import { StatusBanner } from '../components/StatusBanner'
import { anchoredRange } from '../lib/priceRamp'

// docs/ARCHITECTURE.md §6: demo mode ships in Phase 2, not as an afterthought -- this is
// the seeded fictional household's only currently-embedded month
// (Spotlys.Api/Pricing/DemoHousehold.cs). Extending demo mode to a rolling "current month"
// needs a real user profile (Phase 4), not just a later month's seed data.
const DEMO_ZONE = 'NO2'
const DEMO_YEAR = 2026
const DEMO_MONTH = 8

export function Home() {
  const demoBillQuery = useQuery({
    queryKey: ['demo-bill', DEMO_YEAR, DEMO_MONTH],
    queryFn: () => fetchDemoBill(DEMO_YEAR, DEMO_MONTH),
  })

  const statusQuery = useQuery({
    queryKey: ['status'],
    queryFn: fetchStatus,
    refetchInterval: 60 * 1000,
  })

  const hourlyLines = demoBillQuery.data?.bill.hourlyLines ?? []
  const breakEvenExVatOrePerKwh = demoBillQuery.data?.breakEvenExVatOrePerKwh ?? 0

  // Anchored to the whole month's spread, not just the two displayed days, so the ramp
  // reads consistently across every ribbon on the page (docs/DESIGN.md §1).
  const monthEnergyRates = hourlyLines.map((l) => l.energyCostExVatOre / l.kwh)
  const dataMin =
    monthEnergyRates.length > 0 ? Math.min(...monthEnergyRates) : breakEvenExVatOrePerKwh
  const dataMax =
    monthEnergyRates.length > 0 ? Math.max(...monthEnergyRates) : breakEvenExVatOrePerKwh
  const { min: rangeMin, max: rangeMax } = anchoredRange(breakEvenExVatOrePerKwh, dataMin, dataMax)

  // The embedded seed file is 744 contiguous Oslo-local hours starting at local midnight of
  // the 1st (Spotlys.Api/Pricing/DemoHousehold.cs) -- slicing by fixed offsets is exact.
  const day1 = hourlyLines.slice(0, 24)
  const day2 = hourlyLines.slice(24, 48)

  const dayAheadStatus = statusQuery.data?.find((s) => s.jobName === 'IngestDayAheadPrices')

  return (
    <>
      <header className="app-header">
        <h1>Spotlys</h1>
        <nav className="app-nav">
          <Link to="/plan">Planlegg lading</Link>
        </nav>
        <span className="zone-label">{DEMO_ZONE} · Kristiansand</span>
      </header>

      <StatusBanner status={dayAheadStatus} isLoading={statusQuery.isLoading} />

      <p className="demo-note">
        Eksempelhusstand i {DEMO_ZONE} hos Glitre Nett, ordinær strømstøtte. Prisen under er kostnad
        etter støtte for august 2026 -- aldri råpris fra kraftbørsen.
      </p>

      <Ribbon
        label="1. AUGUST 2026"
        statusLabel="eksempeldata"
        hourlyLines={day1}
        rangeMin={rangeMin}
        rangeMax={rangeMax}
      />

      <Ribbon
        label="2. AUGUST 2026"
        statusLabel="eksempeldata"
        hourlyLines={day2}
        rangeMin={rangeMin}
        rangeMax={rangeMax}
      />
    </>
  )
}

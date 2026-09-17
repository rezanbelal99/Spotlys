const API_BASE_URL =
  (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:8080'

export interface PriceObservation {
  zone: string
  hourStartUtc: string
  priceExVatOrePerKwh: number
  source: string
}

export interface FeedStatus {
  jobName: string
  lastRunStartedAtUtc: string | null
  lastRunStatus: string | null
  lastRunRows: number | null
  isStale: boolean
}

/** One hour's line on the demo bill -- cost after support, never a raw spot price
 * (CLAUDE.md rule 1). `spotExVatOrePerKwh` is kept for transparency only. */
export interface DemoHourlyBillLine {
  hourStartUtc: string
  spotExVatOrePerKwh: number
  kwh: number
  energyCostExVatOre: number
  energileddExVatOre: number
  totalExVatOre: number
  isNightRate: boolean
}

export interface DemoBillSummary {
  energyExVatNok: number
  energileddExVatNok: number
  kapasitetsleddExVatNok: number
  forbruksavgiftExVatNok: number
  enovaExVatNok: number
  supplierFeeExVatNok: number
  vatRate: number
  totalExVatNok: number
  totalIncVatNok: number
  topThreeAverageKw: number
  hourlyLines: DemoHourlyBillLine[]
}

export interface DemoBill {
  bill: DemoBillSummary
  breakEvenExVatOrePerKwh: number
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`)
  if (!response.ok) {
    throw new Error(`${path} -> HTTP ${response.status.toString()}`)
  }
  return (await response.json()) as T
}

export function fetchPrices(zone: string, fromUtc: Date, toUtc: Date): Promise<PriceObservation[]> {
  const params = new URLSearchParams({
    from: fromUtc.toISOString(),
    to: toUtc.toISOString(),
  })
  return getJson<PriceObservation[]>(`/api/v1/prices/${zone}?${params.toString()}`)
}

export function fetchStatus(): Promise<FeedStatus[]> {
  return getJson<FeedStatus[]>('/api/v1/status')
}

/** docs/ARCHITECTURE.md §6: the seeded fictional household demo mode serves so the app is
 * explorable without an account -- computed by the real TariffEngine, never raw spot. */
export function fetchDemoBill(year: number, month: number): Promise<DemoBill> {
  const params = new URLSearchParams({ year: year.toString(), month: month.toString() })
  return getJson<DemoBill>(`/api/v1/demo/bill?${params.toString()}`)
}

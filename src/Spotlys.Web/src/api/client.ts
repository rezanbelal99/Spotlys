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

export interface QuantileForecastHour {
  targetHourUtc: string
  q05: number
  q25: number
  q50: number
  q75: number
  q95: number
}

export interface ForecastFan {
  zone: string
  issuedAtUtc: string
  hours: QuantileForecastHour[]
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
  const response = await fetch(`${API_BASE_URL}${path}`, { credentials: 'include' })
  if (!response.ok) {
    throw new Error(`${path} -> HTTP ${response.status.toString()}`)
  }
  return (await response.json()) as T
}

/** `credentials: 'include'` on every call -- cookie auth (docs/ARCHITECTURE.md §6) only
 * works cross-origin (the Vite dev server on :5173, the API on :8080) if the browser is
 * told to send/accept the cookie, matching Program.cs's `AllowCredentials()` CORS policy. */
async function sendJson<TResponse>(
  method: 'POST' | 'DELETE',
  path: string,
  body?: unknown,
): Promise<TResponse> {
  const init: RequestInit = { method, credentials: 'include' }
  if (body !== undefined) {
    init.headers = { 'Content-Type': 'application/json' }
    init.body = JSON.stringify(body)
  }
  const response = await fetch(`${API_BASE_URL}${path}`, init)
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as {
      title?: string
      detail?: string
    } | null
    throw new Error(
      problem?.detail ?? problem?.title ?? `${path} -> HTTP ${response.status.toString()}`,
    )
  }

  // A 200 with no body (e.g. logout) has nothing for response.json() to parse.
  const text = await response.text()
  return (text === '' ? undefined : JSON.parse(text)) as TResponse
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

export function fetchForecast(zone: string, horizonHours: number): Promise<ForecastFan> {
  const params = new URLSearchParams({ horizon: horizonHours.toString() })
  return getJson<ForecastFan>(`/api/v1/forecast/${zone}?${params.toString()}`)
}

/** docs/ARCHITECTURE.md §6: the seeded fictional household demo mode serves so the app is
 * explorable without an account -- computed by the real TariffEngine, never raw spot. */
export function fetchDemoBill(year: number, month: number): Promise<DemoBill> {
  const params = new URLSearchParams({ year: year.toString(), month: month.toString() })
  return getJson<DemoBill>(`/api/v1/demo/bill?${params.toString()}`)
}

// --- Accounts (Phase 4, part 5's /plan needs a signed-in user + a meter) ---

export interface Account {
  id: string
  email: string
}

export function register(email: string, password: string): Promise<Account> {
  return sendJson<Account>('POST', '/api/v1/account/register', { email, password })
}

export function login(email: string, password: string): Promise<Account> {
  return sendJson<Account>('POST', '/api/v1/account/login', { email, password })
}

export async function logout(): Promise<void> {
  await sendJson<unknown>('POST', '/api/v1/account/logout')
}

export interface MeterProfile {
  id: string
  zone: string
  gridCompanyId: string
  supportScheme: string
  isCabin: boolean
  supplierMarkupExVatOrePerKwh: number
  supplierMonthlyFeeExVatNok: number
  createdAtUtc: string
}

export function listMeters(): Promise<MeterProfile[]> {
  return getJson<MeterProfile[]>('/api/v1/meters')
}

export function createMeter(input: {
  zone: string
  gridCompanyId: string
  supportScheme: string
  isCabin: boolean
  supplierMarkupExVatOrePerKwh: number
  supplierMonthlyFeeExVatNok: number
}): Promise<MeterProfile> {
  return sendJson<MeterProfile>('POST', '/api/v1/meters', input)
}

// --- The plan itself (docs/FORECASTING.md §8) ---

export interface HourAllocation {
  hourStartUtc: string
  allocatedKwh: number
  marginalCostExVatOrePerKwh: number
}

export interface CostRangeDto {
  lowExVatNok: number
  highExVatNok: number
  expectedExVatNok: number
}

export interface PlanResponse {
  allocations: HourAllocation[]
  chosenCost: CostRangeDto
  counterfactual: { label: string; cost: CostRangeDto }
  bindingConstraint: string
  isFullyScheduled: boolean
}

export function planCharge(
  meterProfileId: string,
  load: {
    energyKwh: number
    maxPowerKw: number
    minPowerKw: number
    notBefore: string
    deadline: string
    interruptible: boolean
  },
): Promise<PlanResponse> {
  return sendJson<PlanResponse>('POST', '/api/v1/plan', { meterProfileId, load })
}

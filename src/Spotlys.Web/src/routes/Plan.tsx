import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import {
  createMeter,
  fetchForecast,
  listMeters,
  login,
  planCharge,
  register,
  type PlanResponse,
} from '../api/client'
import { PlanRibbon } from '../components/PlanRibbon'

const ZONE = 'NO2'
const GRID_COMPANY_ID = 'glitre-nett'
const HORIZON_HOURS = 48

// docs/DESIGN.md §5's full motion spec (the animated snapping bracket) is Phase 5 polish,
// same as the home ribbon's tide entrance -- src/index.css's own header note already says
// so. This route's bracket highlight uses a plain CSS transition, not motion/react, to stay
// consistent with that existing, deliberate phasing rather than introducing the animation
// library a phase early.
export function Plan() {
  const queryClient = useQueryClient()
  const metersQuery = useQuery({ queryKey: ['meters'], queryFn: listMeters, retry: false })

  if (metersQuery.isLoading) {
    return <p className="plan-status">Laster …</p>
  }

  if (metersQuery.isError) {
    return (
      <AuthGate
        onSignedIn={() => {
          void queryClient.invalidateQueries({ queryKey: ['meters'] })
        }}
      />
    )
  }

  const meters = metersQuery.data ?? []
  const firstMeter = meters[0]
  if (!firstMeter) {
    return (
      <CreateMeterForm
        onCreated={() => {
          void queryClient.invalidateQueries({ queryKey: ['meters'] })
        }}
      />
    )
  }

  return <LoadPlanner meterId={firstMeter.id} />
}

function AuthGate({ onSignedIn }: { onSignedIn: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [mode, setMode] = useState<'login' | 'register'>('register')
  const [error, setError] = useState<string | null>(null)

  const submit = useMutation({
    mutationFn: () => (mode === 'register' ? register(email, password) : login(email, password)),
    onSuccess: onSignedIn,
    onError: (err: Error) => {
      setError(err.message)
    },
  })

  return (
    <div className="plan-auth">
      <p>
        <Link to="/">← Tilbake</Link>
      </p>
      <h2>{mode === 'register' ? 'Opprett konto' : 'Logg inn'}</h2>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          setError(null)
          submit.mutate()
        }}
      >
        <label>
          E-post
          <input
            type="email"
            required
            value={email}
            onChange={(e) => {
              setEmail(e.target.value)
            }}
          />
        </label>
        <label>
          Passord
          <input
            type="password"
            required
            minLength={10}
            value={password}
            onChange={(e) => {
              setPassword(e.target.value)
            }}
          />
        </label>
        {error !== null && <p className="plan-error">{error}</p>}
        <button type="submit" disabled={submit.isPending}>
          {mode === 'register' ? 'Opprett konto' : 'Logg inn'}
        </button>
      </form>
      <button
        type="button"
        className="plan-link-button"
        onClick={() => {
          setMode(mode === 'register' ? 'login' : 'register')
        }}
      >
        {mode === 'register' ? 'Har du allerede konto? Logg inn' : 'Ny bruker? Opprett konto'}
      </button>
    </div>
  )
}

function CreateMeterForm({ onCreated }: { onCreated: () => void }) {
  const [scheme, setScheme] = useState<'stromstotte' | 'norgespris'>('stromstotte')
  const submit = useMutation({
    mutationFn: () =>
      createMeter({
        zone: ZONE,
        gridCompanyId: GRID_COMPANY_ID,
        supportScheme: scheme,
        isCabin: false,
        supplierMarkupExVatOrePerKwh: 0,
        supplierMonthlyFeeExVatNok: 0,
      }),
    onSuccess: onCreated,
  })

  return (
    <div className="plan-auth">
      <h2>Legg til måler</h2>
      <p className="demo-note">{ZONE} hos Glitre Nett -- samme oppsett som eksempelhusstanden.</p>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          submit.mutate()
        }}
      >
        <label>
          <input
            type="radio"
            name="scheme"
            checked={scheme === 'stromstotte'}
            onChange={() => {
              setScheme('stromstotte')
            }}
          />
          Ordinær strømstøtte
        </label>
        <label>
          <input
            type="radio"
            name="scheme"
            checked={scheme === 'norgespris'}
            onChange={() => {
              setScheme('norgespris')
            }}
          />
          Norgespris
        </label>
        <button type="submit" disabled={submit.isPending}>
          Legg til
        </button>
      </form>
    </div>
  )
}

function LoadPlanner({ meterId }: { meterId: string }) {
  const [energyKwh, setEnergyKwh] = useState(40)
  const [maxPowerKw, setMaxPowerKw] = useState(7)
  const [deadlineHoursFromNow, setDeadlineHoursFromNow] = useState(24)
  const [result, setResult] = useState<PlanResponse | null>(null)

  const forecastQuery = useQuery({
    queryKey: ['forecast', ZONE, HORIZON_HOURS],
    queryFn: () => fetchForecast(ZONE, HORIZON_HOURS),
  })

  const submit = useMutation({
    mutationFn: () => {
      const notBefore = new Date()
      const deadline = new Date(notBefore.getTime() + deadlineHoursFromNow * 3600_000)
      return planCharge(meterId, {
        energyKwh,
        maxPowerKw,
        minPowerKw: 0,
        notBefore: notBefore.toISOString(),
        deadline: deadline.toISOString(),
        interruptible: true,
      })
    },
    onSuccess: setResult,
  })

  return (
    <div className="plan-page">
      <p>
        <Link to="/">← Tilbake</Link>
      </p>
      <h2>Planlegg lading</h2>
      <form
        className="plan-form"
        onSubmit={(e) => {
          e.preventDefault()
          submit.mutate()
        }}
      >
        <label>
          Energi (kWh)
          <input
            type="number"
            min={0.1}
            step={0.1}
            value={energyKwh}
            onChange={(e) => {
              setEnergyKwh(Number(e.target.value))
            }}
          />
        </label>
        <label>
          Maks effekt (kW)
          <input
            type="number"
            min={0.1}
            step={0.1}
            value={maxPowerKw}
            onChange={(e) => {
              setMaxPowerKw(Number(e.target.value))
            }}
          />
        </label>
        <label>
          Klar om (timer)
          <input
            type="number"
            min={1}
            max={HORIZON_HOURS}
            value={deadlineHoursFromNow}
            onChange={(e) => {
              setDeadlineHoursFromNow(Number(e.target.value))
            }}
          />
        </label>
        <button type="submit" disabled={submit.isPending}>
          Lag ladeplan
        </button>
      </form>

      {submit.isError && <p className="plan-error">{submit.error.message}</p>}

      {forecastQuery.data && (
        <PlanRibbon forecast={forecastQuery.data} allocations={result?.allocations ?? []} />
      )}

      {result && (
        <div className="plan-result">
          <p className="plan-headline">
            {result.isFullyScheduled
              ? `${result.chosenCost.lowExVatNok.toFixed(0)}–${result.chosenCost.highExVatNok.toFixed(0)} kr, mot ${result.counterfactual.cost.lowExVatNok.toFixed(0)}–${result.counterfactual.cost.highExVatNok.toFixed(0)} kr hvis du ${result.counterfactual.label.toLowerCase()}.`
              : `Rakk ikke hele ladingen innen fristen -- begrenset av ${bindingConstraintLabel(result.bindingConstraint)}.`}
          </p>
          <p className="demo-note">
            Begrenset av: {bindingConstraintLabel(result.bindingConstraint)}
          </p>
        </div>
      )}
    </div>
  )
}

function bindingConstraintLabel(constraint: string): string {
  switch (constraint) {
    case 'deadline':
      return 'at ladingen skal være klar til fristen'
    case 'capacity_step':
      return 'effekttrinnet ditt'
    case 'max_power':
      return 'laderens maks effekt'
    default:
      return constraint
  }
}

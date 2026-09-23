import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchModelSkill } from '../api/client'

// docs/ROADMAP.md Phase 3/4: NO2 is the only zone with a trained, backtested model so far
// (Phase 3's own scope cut, still true here -- see docs/adr entries on per-zone models).
const ZONE = 'NO2'

function formatSkill(skill: number): string {
  const percent = (skill * 100).toFixed(1)
  return skill >= 0 ? `+${percent} %` : `${percent} %`
}

function formatRegret(nok: number): string {
  // Negative regret means the forecast-driven schedule beat the naive comparison -- stated
  // plainly either way (docs/DOMAIN.md §7: never smooth an unflattering number away).
  const rounded = nok.toFixed(1)
  return nok <= 0 ? `${Math.abs(nok).toFixed(1)} kr billigere` : `${rounded} kr dyrere`
}

export function Model() {
  const skillQuery = useQuery({
    queryKey: ['model-skill', ZONE],
    queryFn: () => fetchModelSkill(ZONE),
  })

  const report = skillQuery.data
  const maxAbsSkill = report
    ? Math.max(0.05, ...report.byLeadBucket.map((b) => Math.abs(b.skillVsB1)))
    : 0.05

  return (
    <>
      <header className="app-header">
        <h1>Spotlys</h1>
        <nav className="app-nav">
          <Link to="/">Forsiden</Link>
        </nav>
        <span className="zone-label">{ZONE} · modellens treffsikkerhet</span>
      </header>

      <p className="demo-note">
        Denne siden er offentlig og krever ingen konto. Tallene kommer fra en walk-forward backtest
        (docs/FORECASTING.md §6) -- aldri fra modellens egen treningsperiode -- og rapporteres slik
        de faktisk ble målt, også der modellen taper mot en enkel referanse.
      </p>

      {skillQuery.isLoading && <p className="plan-status">Laster modelltall...</p>}

      {skillQuery.isError && (
        <p className="plan-error">Klarte ikke å hente modelltall akkurat nå.</p>
      )}

      {skillQuery.isSuccess && report === null && (
        <p className="plan-status">
          Ingen backtest-rapport er publisert for {ZONE} ennå. Rapporten genereres av
          model.yml-jobben (docs/ENGINEERING.md §3) og vises her så snart den første kjøringen er
          fullført.
        </p>
      )}

      {report && (
        <>
          <section className="model-section">
            <h2 className="model-section-title">Treffsikkerhet mot enkel referanse (B1)</h2>
            <p className="model-section-note">
              Skår over 0 % betyr at modellen slår B1 (samme time forrige uke) -- den vanligste,
              vanskeligste referansen å slå (docs/FORECASTING.md §2). Negativ skår vises som
              negativ, ikke skjult.
            </p>
            <svg
              className="model-skill-chart"
              viewBox={`0 0 320 ${(report.byLeadBucket.length * 32).toString()}`}
              role="img"
              aria-label={`Modellens skår mot referanse B1, per tidshorisont, for ${ZONE}`}
            >
              {report.byLeadBucket.map((bucket, i) => {
                const barWidth = (Math.abs(bucket.skillVsB1) / maxAbsSkill) * 140
                const isPositive = bucket.skillVsB1 >= 0
                const y = i * 32 + 6
                return (
                  <g key={bucket.leadBucket}>
                    <text x="0" y={y + 13} className="model-skill-label">
                      {bucket.leadBucket}
                    </text>
                    <line x1="160" y1={y} x2="160" y2={y + 20} className="model-skill-axis" />
                    <rect
                      x={isPositive ? 160 : 160 - barWidth}
                      y={y}
                      width={barWidth}
                      height="20"
                      className={
                        isPositive ? 'model-skill-bar-positive' : 'model-skill-bar-negative'
                      }
                    />
                    <text
                      x={isPositive ? 164 + barWidth : 156 - barWidth}
                      y={y + 14}
                      textAnchor={isPositive ? 'start' : 'end'}
                      className="model-skill-value"
                    >
                      {formatSkill(bucket.skillVsB1)}
                    </text>
                  </g>
                )
              })}
            </svg>
            <table className="model-skill-table">
              <caption className="visually-hidden">
                MAE og dekningsgrad for 50% og 90% intervallet, per tidshorisont
              </caption>
              <thead>
                <tr>
                  <th scope="col">Horisont</th>
                  <th scope="col">MAE (øre/kWh)</th>
                  <th scope="col">Dekning 50 %</th>
                  <th scope="col">Dekning 90 %</th>
                </tr>
              </thead>
              <tbody>
                {report.byLeadBucket.map((bucket) => (
                  <tr key={bucket.leadBucket}>
                    <td>{bucket.leadBucket}</td>
                    <td>{bucket.maeOrePerKwh.toFixed(1)}</td>
                    <td>{(bucket.coverage50 * 100).toFixed(0)} %</td>
                    <td>{(bucket.coverage90 * 100).toFixed(0)} %</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>

          <section className="model-section">
            <h2 className="model-section-title">Betyr det noe for regningen?</h2>
            <p className="model-section-note">
              Beslutningsregret (docs/FORECASTING.md §6): kjører ladeplanleggeren på modellens
              prognose og på fasiten (perfekt forhåndskunnskap), og sammenligner den faktiske
              regningen -- ikke bare prisfeilen. Basert på {report.regret.backtestDays} dager i
              backtesten.
            </p>
            <p className="model-headline">
              Mot &laquo;lad med en gang&raquo;:{' '}
              {formatRegret(report.regret.regretVsChargeOnArrivalNok)}
            </p>
            <p className="model-headline">
              Mot &laquo;lad alltid kl. 02&raquo;:{' '}
              {formatRegret(report.regret.regretVsAlways0200Nok)}
            </p>
          </section>

          <p className="model-generated-at">
            Rapport generert {new Date(report.reportGeneratedAtUtc).toLocaleString('nb-NO')}.
          </p>
        </>
      )}
    </>
  )
}

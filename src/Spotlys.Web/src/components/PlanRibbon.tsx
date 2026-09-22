import { scaleLinear } from 'd3-scale'
import { priceToColor } from '../lib/priceRamp'
import type { ForecastFan, HourAllocation } from '../api/client'

const CELL_WIDTH = 16
const CELL_HEIGHT = 48
const CELL_GAP = 2

interface PlanRibbonProps {
  forecast: ForecastFan
  /** The chosen hours, if a plan has been computed yet -- empty before the first submit. */
  allocations: readonly HourAllocation[]
}

// Same hand-written-SVG, D3-for-geometry-only discipline as Ribbon.tsx (docs/ARCHITECTURE.md
// §7, ADR 0004) -- not literally sharing a component with Ribbon.tsx, since the two work
// from different data shapes (a bill's hourly lines vs a forecast fan's quantiles) and
// forcing a shared abstraction over that difference isn't earning its keep yet. The
// "bracket" (docs/DESIGN.md §5) is the highlighted border around allocated cells; the
// animated snap-transition version is Phase 5 motion-spec work (see Plan.tsx's own note).
export function PlanRibbon({ forecast, allocations }: PlanRibbonProps) {
  const hours = forecast.hours
  const width = hours.length * (CELL_WIDTH + CELL_GAP) - CELL_GAP
  const xScale = scaleLinear().domain([0, hours.length]).range([0, width])

  const allocatedByHour = new Map(allocations.map((a) => [a.hourStartUtc, a]))
  const expectedValue = (h: (typeof hours)[number]) =>
    h.q05 * 0.15 + h.q25 * 0.225 + h.q50 * 0.25 + h.q75 * 0.225 + h.q95 * 0.15
  const expectedValues = hours.map(expectedValue)
  const rangeMin = Math.min(...expectedValues)
  const rangeMax = Math.max(...expectedValues)

  return (
    <div className="plan-ribbon">
      <svg
        role="img"
        aria-label="Prisprognose med foreslåtte ladetimer merket"
        viewBox={`0 0 ${width.toString()} ${CELL_HEIGHT.toString()}`}
        width="100%"
        height={CELL_HEIGHT}
      >
        {hours.map((hour, i) => {
          const x = xScale(i)
          const expected = expectedValue(hour)
          const allocation = allocatedByHour.get(hour.targetHourUtc)
          const localHour = new Date(hour.targetHourUtc).getUTCHours()

          return (
            <g key={hour.targetHourUtc}>
              <rect
                x={x}
                y={0}
                width={CELL_WIDTH}
                height={CELL_HEIGHT}
                rx={2}
                fill={priceToColor(expected, rangeMin, rangeMax)}
                stroke={allocation ? 'var(--ice-100)' : 'none'}
                strokeWidth={allocation ? 2 : 0}
                className="plan-ribbon-cell"
              >
                <title>
                  {new Date(hour.targetHourUtc).toLocaleString('nb-NO', {
                    weekday: 'short',
                    hour: '2-digit',
                  })}{' '}
                  – {expected.toFixed(0)} øre/kWh forventet
                  {allocation ? ` · lader ${allocation.allocatedKwh.toFixed(1)} kWh her` : ''}
                </title>
              </rect>
              {localHour === 0 && (
                <text x={x} y={CELL_HEIGHT + 12} fontSize={8} fill="var(--birch-200)">
                  {new Date(hour.targetHourUtc).toLocaleDateString('nb-NO', { weekday: 'short' })}
                </text>
              )}
            </g>
          )
        })}
      </svg>
    </div>
  )
}

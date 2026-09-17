import { scaleLinear } from 'd3-scale'
import { priceToColor } from '../lib/priceRamp'
import type { DemoHourlyBillLine } from '../api/client'

const CELL_COUNT = 24
const CELL_WIDTH = 20
const CELL_HEIGHT = 48
const CELL_GAP = 2
const RIBBON_WIDTH = CELL_COUNT * (CELL_WIDTH + CELL_GAP) - CELL_GAP

interface RibbonProps {
  label: string
  statusLabel: string
  /** Exactly one Oslo-local calendar day's worth of bill lines, hour 0..23 in order. */
  hourlyLines: readonly DemoHourlyBillLine[]
  rangeMin: number
  rangeMax: number
}

// Hand-written SVG, D3 for the colour scale's geometry only (docs/ARCHITECTURE.md §7): no
// charting library. One bar per hour, constant height -- price is read from fill colour,
// never bar height (docs/DESIGN.md §4: "not a bar chart").
//
// The fill colour and the hour readout both come from the bill's per-hour lines, never a
// raw spot price (CLAUDE.md rule 1) -- `energyCostExVatOre / kwh` reconstructs the
// after-support energy rate the engine actually charged for that hour.
export function Ribbon({ label, statusLabel, hourlyLines, rangeMin, rangeMax }: RibbonProps) {
  const xScale = scaleLinear().domain([0, CELL_COUNT]).range([0, RIBBON_WIDTH])
  const hourLabel = (h: number): string => `${h.toString().padStart(2, '0')}:00`

  return (
    <div className="ribbon">
      <div className="ribbon-header">
        <span className="ribbon-label">{label}</span>
        <span className="ribbon-status">{statusLabel}</span>
      </div>
      <svg
        role="img"
        aria-label={`${label}: ${statusLabel}`}
        viewBox={`0 0 ${RIBBON_WIDTH.toString()} ${CELL_HEIGHT.toString()}`}
        width="100%"
        height={CELL_HEIGHT}
      >
        {hourlyLines.map((line, hour) => {
          const x = xScale(hour)
          const energyRateAfterSupportExVatOrePerKwh = line.energyCostExVatOre / line.kwh
          const energileddRateExVatOrePerKwh = line.energileddExVatOre / line.kwh

          return (
            <rect
              key={line.hourStartUtc}
              x={x}
              y={0}
              width={CELL_WIDTH}
              height={CELL_HEIGHT}
              rx={2}
              fill={priceToColor(energyRateAfterSupportExVatOrePerKwh, rangeMin, rangeMax)}
            >
              <title>
                {hourLabel(hour)} – {energyRateAfterSupportExVatOrePerKwh.toFixed(1)} øre/kWh
                etter støtte · nettleie {line.isNightRate ? 'natt' : 'dag'}{' '}
                {energileddRateExVatOrePerKwh.toFixed(1)} øre/kWh ·{' '}
                {(line.totalExVatOre / 100).toFixed(2)} kr denne timen
              </title>
            </rect>
          )
        })}
      </svg>
    </div>
  )
}

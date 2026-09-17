/**
 * The ice -> ember price ramp (docs/DESIGN.md §2), interpolated in Oklch so the ramp is
 * perceptually even and the midpoint doesn't turn muddy. No charting/color library is used
 * (culori isn't named in docs/ARCHITECTURE.md §7) -- this is a small hand-written
 * Oklch<->sRGB conversion, per DESIGN.md §2's own suggestion ("a hand-written oklch()
 * interpolation; CSS oklch() has universal support now").
 *
 * Anchored to the user's break-even price (docs/DESIGN.md §1), not the visible range's
 * min/max -- `anchoredRange` below builds a [min, max] that's symmetric around the anchor
 * and still covers the full data spread, so `priceToColor`'s existing min/max contract
 * places the anchor at exactly t=0.5 (the "slack" stop) without needing its own code path.
 */

interface OklchStop {
  t: number // position along the ramp, 0..1
  l: number
  c: number
  h: number // degrees
}

// Oklch coordinates for docs/DESIGN.md §2's six named ramp stops, precomputed from their
// hex values (ice-300, shallow, deep | slack, ember-400, ember-600).
const RAMP_STOPS: readonly OklchStop[] = [
  { t: 0, l: 0.8354, c: 0.0751, h: 215.45 }, // ice
  { t: 0.2, l: 0.6555, c: 0.085, h: 220.3 }, // shallow
  { t: 0.4, l: 0.4848, c: 0.0765, h: 224.08 }, // deep
  { t: 0.5, l: 0.5386, c: 0.0052, h: 214.34 }, // slack (the anchor)
  { t: 0.75, l: 0.767, c: 0.1154, h: 66.26 }, // ember
  { t: 1, l: 0.5812, c: 0.1446, h: 37.76 }, // spike
]

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t
}

function lerpHue(a: number, b: number, t: number): number {
  const diff = ((((b - a) % 360) + 540) % 360) - 180
  return (a + diff * t + 360) % 360
}

function oklchToSrgbString(l: number, c: number, h: number): string {
  const hRad = (h * Math.PI) / 180
  const a = c * Math.cos(hRad)
  const b = c * Math.sin(hRad)

  const l_ = l + 0.3963377774 * a + 0.2158037573 * b
  const m_ = l - 0.1055613458 * a - 0.0638541728 * b
  const s_ = l - 0.0894841775 * a - 1.291485548 * b

  const ll = l_ ** 3
  const mm = m_ ** 3
  const ss = s_ ** 3

  const rl = 4.0767416621 * ll - 3.3077115913 * mm + 0.2309699292 * ss
  const gl = -1.2684380046 * ll + 2.6097574011 * mm - 0.3413193965 * ss
  const bl = -0.0041960863 * ll - 0.7034186147 * mm + 1.707614701 * ss

  const toSrgb = (x: number): number => {
    const clamped = Math.min(1, Math.max(0, x))
    return clamped <= 0.0031308 ? 12.92 * clamped : 1.055 * clamped ** (1 / 2.4) - 0.055
  }

  const r = Math.round(toSrgb(rl) * 255)
  const g = Math.round(toSrgb(gl) * 255)
  const bch = Math.round(toSrgb(bl) * 255)
  return `rgb(${r.toString()}, ${g.toString()}, ${bch.toString()})`
}

function colorAt(t: number): string {
  const clamped = Math.min(1, Math.max(0, t))

  let lower = RAMP_STOPS[0]
  let upper = RAMP_STOPS[RAMP_STOPS.length - 1]
  if (!lower || !upper) {
    return 'rgb(128, 128, 128)'
  }

  for (let i = 0; i < RAMP_STOPS.length - 1; i++) {
    const a = RAMP_STOPS[i]
    const b = RAMP_STOPS[i + 1]
    if (a && b && clamped >= a.t && clamped <= b.t) {
      lower = a
      upper = b
      break
    }
  }

  const span = upper.t - lower.t
  const localT = span === 0 ? 0 : (clamped - lower.t) / span

  return oklchToSrgbString(
    lerp(lower.l, upper.l, localT),
    lerp(lower.c, upper.c, localT),
    lerpHue(lower.h, upper.h, localT),
  )
}

/** Maps a price into the ramp for a given [min, max]. */
export function priceToColor(price: number, min: number, max: number): string {
  if (max <= min) {
    return colorAt(0.5)
  }
  return colorAt((price - min) / (max - min))
}

/**
 * A [min, max] symmetric around `anchor` (the user's break-even price) and wide enough to
 * cover [dataMin, dataMax] on whichever side needs more room -- so a day that's uniformly
 * cheap or uniformly expensive still shows the anchor at the ramp's midpoint rather than
 * silently re-centring on the visible range (docs/DESIGN.md §1: "a boring day *looks*
 * boring", never dramatised by a shifting scale).
 */
export function anchoredRange(
  anchor: number,
  dataMin: number,
  dataMax: number,
): { min: number; max: number } {
  const spread = Math.max(anchor - dataMin, dataMax - anchor, 1)
  return { min: anchor - spread, max: anchor + spread }
}

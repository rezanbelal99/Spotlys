import { describe, expect, it } from 'vitest'
import { priceToColor } from './priceRamp'

describe('priceToColor', () => {
  it('returns a valid rgb() string for prices across the range', () => {
    const rgbPattern = /^rgb\(\d+, \d+, \d+\)$/
    expect(priceToColor(0, 0, 100)).toMatch(rgbPattern)
    expect(priceToColor(50, 0, 100)).toMatch(rgbPattern)
    expect(priceToColor(100, 0, 100)).toMatch(rgbPattern)
  })

  it('does not throw when min equals max', () => {
    expect(() => priceToColor(42, 42, 42)).not.toThrow()
  })

  it('clamps prices outside the given range instead of extrapolating oddly', () => {
    const belowRange = priceToColor(-50, 0, 100)
    const atMin = priceToColor(0, 0, 100)
    expect(belowRange).toBe(atMin)

    const aboveRange = priceToColor(500, 0, 100)
    const atMax = priceToColor(100, 0, 100)
    expect(aboveRange).toBe(atMax)
  })
})

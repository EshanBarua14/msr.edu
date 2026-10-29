import { describe, expect, it } from 'vitest'
import { changedFields, niceMax, periodLabel } from './ops'

describe('ops helpers', () => {
  it('lists only changed fields on an update', () => {
    const rows = changedFields({ before: { price_minor: 100, name: 'A', updated_at: 1 }, after: { price_minor: 200, name: 'A', updated_at: 2 } })
    expect(rows).toEqual([{ field: 'price_minor', before: 100, after: 200 }])
    expect(changedFields({ before: null, after: { a: 1 } })).toEqual([{ field: 'a', before: undefined, after: 1 }])
  })

  it('formats periods and axis maxima', () => {
    expect(periodLabel('2026-10-29')).toBe('29 Oct')
    expect(periodLabel('2026-03')).toBe('Mar 26')
    expect(niceMax(0)).toBe(1)
    expect(niceMax(130_000)).toBe(200_000)
    expect(niceMax(500)).toBe(500)
    expect(niceMax(501)).toBe(1000)
  })
})

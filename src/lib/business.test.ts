import { describe, expect, it } from 'vitest'
import { formatMoney } from './business'

describe('formatMoney', () => {
  it('formats minor units as BDT', () => {
    expect(formatMoney(149900)).toMatch(/1,499/)
  })
})

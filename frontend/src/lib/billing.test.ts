import { describe, expect, it } from 'vitest'
import { isFinal, paymentTone, periodLabel, statusLabel, toMajor, toMinor } from './billing'

describe('billing helpers', () => {
  it('converts taka to paisa without float drift', () => {
    expect(toMinor('1500')).toBe(150000)
    expect(toMinor('19.99')).toBe(1999)
    expect(toMinor('0.1')).toBe(10)
    expect(toMinor('')).toBeNull()
    expect(toMinor('-5')).toBeNull()
    expect(toMinor('1.234')).toBeNull()
    expect(toMajor(150000)).toBe('1500')
    expect(toMajor(null)).toBe('')
  })

  it('labels and tones statuses', () => {
    expect(statusLabel('partially_refunded')).toBe('Partially refunded')
    expect(paymentTone('successful')).toBe('success')
    expect(paymentTone('failed')).toBe('danger')
    expect(isFinal('pending')).toBe(false)
    expect(isFinal('expired')).toBe(true)
    expect(periodLabel('custom_days', 90)).toBe('every 90 days')
  })
})

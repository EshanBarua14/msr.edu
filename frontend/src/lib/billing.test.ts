import { describe, expect, it } from 'vitest'
import { expiringSoon, isFinal, paymentTone, periodLabel, statusLabel, toMajor, toMinor } from './billing'

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

describe('expiringSoon', () => {
  it('keeps active access ending inside the window', () => {
    const now = Date.parse('2026-01-01T00:00:00Z')
    const row = (id: string, status: string, expiresAt: string | null) =>
      ({ id, kind: 'course', title: id, courseSlug: null, source: 'purchase', status, statusReason: null, startsAt: '', expiresAt }) as const
    const out = expiringSoon([row('a', 'active', '2026-01-05T00:00:00Z'), row('b', 'active', '2026-03-01T00:00:00Z'),
      row('c', 'active', null), row('d', 'revoked', '2026-01-02T00:00:00Z')], 14, now)
    expect(out.map((a) => a.id)).toEqual(['a'])
  })
})

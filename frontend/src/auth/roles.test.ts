import { describe, expect, it } from 'vitest'
import { hasAnyRole, homeFor, isStaff, safeReturnPath } from './roles'

describe('roles', () => {
  it('treats owner_admin as satisfying any requirement', () => {
    expect(hasAnyRole(['owner_admin'], ['finance_manager'])).toBe(true)
  })

  it('denies learners staff-only requirements', () => {
    expect(hasAnyRole(['learner'], ['content_manager', 'finance_manager'])).toBe(false)
    expect(isStaff(['learner'])).toBe(false)
  })

  it('routes staff to admin and learners to app', () => {
    expect(homeFor(['learner', 'support_staff'])).toBe('/admin')
    expect(homeFor(['learner'])).toBe('/app')
  })

  it('rejects open-redirect return paths', () => {
    expect(safeReturnPath('/app/courses')).toBe('/app/courses')
    expect(safeReturnPath('//evil.com')).toBeNull()
    expect(safeReturnPath('/\\evil.com')).toBeNull()
    expect(safeReturnPath('https://evil.com')).toBeNull()
    expect(safeReturnPath(null)).toBeNull()
  })
})

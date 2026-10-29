import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError, googleLoginUrl } from './api'

afterEach(() => vi.unstubAllGlobals())

function stubFetch(status: number, body?: unknown) {
  const fn = vi.fn(async () => new Response(body === undefined ? null : JSON.stringify(body), { status }))
  vi.stubGlobal('fetch', fn)
  return fn
}

describe('api', () => {
  it('sends the CSRF header only on state-changing requests', async () => {
    const fetch = stubFetch(200, { ok: true })
    await api('/api/x')
    await api('/api/y', { json: { a: 1 } })
    const [, get] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    const [, post] = fetch.mock.calls[1] as unknown as [string, RequestInit]
    expect(get.headers).not.toHaveProperty('X-MSR-CSRF')
    expect(post.method).toBe('POST')
    expect(post.headers).toHaveProperty('X-MSR-CSRF', '1')
  })

  it('throws ApiError with the status', async () => {
    stubFetch(401, { title: 'Unauthorized' })
    await expect(api('/api/auth/me')).rejects.toMatchObject({ status: 401 })
    await expect(api('/api/auth/me')).rejects.toBeInstanceOf(ApiError)
  })

  it('builds the Google login URL with an encoded return path', () => {
    expect(googleLoginUrl('/app?x=1')).toBe('/api/auth/google/login?returnUrl=%2Fapp%3Fx%3D1')
  })
})

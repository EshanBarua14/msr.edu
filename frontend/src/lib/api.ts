/** Thin client for the MSR.edu API. Same origin: the session is an HttpOnly cookie. */

export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, body: unknown) {
    super(typeof body === 'object' && body && 'title' in body ? String(body.title) : `Request failed (${status})`)
    this.status = status
    this.body = body
  }
}

// The API rejects state-changing requests without this header (CSRF protection).
const CSRF_HEADER = 'X-MSR-CSRF'

export async function api<T>(path: string, init: RequestInit & { json?: unknown } = {}): Promise<T> {
  const { json, headers, ...rest } = init
  const method = (rest.method ?? (json !== undefined ? 'POST' : 'GET')).toUpperCase()
  const res = await fetch(path, {
    ...rest,
    method,
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(method !== 'GET' && method !== 'HEAD' ? { [CSRF_HEADER]: '1' } : {}),
      ...headers,
    },
    body: json !== undefined ? JSON.stringify(json) : rest.body,
  })
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const body = text ? safeJson(text) : null
  if (!res.ok) throw new ApiError(res.status, body)
  return body as T
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

export const googleLoginUrl = (returnTo?: string) =>
  `/api/auth/google/login${returnTo ? `?returnUrl=${encodeURIComponent(returnTo)}` : ''}`

import { expect, type APIRequestContext, type Page } from '@playwright/test'

const csrf = { 'X-MSR-CSRF': '1' }
export const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5080'
export const unique = (prefix: string) => `${prefix}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`

/** Dev sign-in (Development only). The page's cookies are shared with page.request. */
export async function signIn(page: Page, email: string, roles: string[] = []) {
  const res = await page.request.post('/api/auth/dev-login', { headers: csrf, data: { email, roles } })
  expect(res.ok(), await res.text()).toBeTruthy()
}

export async function call<T = unknown>(request: APIRequestContext, method: string, url: string, data?: unknown): Promise<T> {
  const res = await request.fetch(url, { method, headers: csrf, data })
  expect(res.ok(), `${method} ${url}: ${res.status()} ${await res.text()}`).toBeTruthy()
  const text = await res.text()
  return (text ? JSON.parse(text) : null) as T
}

export interface SeededCourse { id: string; slug: string; title: string; paidLessonId: string; quizId: string }

/** Builds a published, priced course with a free preview, a paid lesson and a one-question quiz, as staff would. */
export async function seedCourse(page: Page, priceMinor = 150_000): Promise<SeededCourse> {
  const slug = unique('course')
  const title = `Algebra ${slug.slice(-6)}`
  const r = page.request
  const course = await call<{ id: string }>(r, 'POST', '/api/admin/courses', {
    slug, title, summary: 'Equations from scratch.', description: null, coverUrl: null, categoryId: null, status: 'draft', isFree: false, position: 0,
  })
  const lesson = (s: string, t: string, isPreview: boolean) => call<{ id: string }>(r, 'POST', `/api/admin/courses/${course.id}/lessons`, {
    moduleId: null, slug: s, title: t, summary: null, kind: 'text', durationSeconds: 300, isPreview, status: 'published',
    body: `${t}: to solve x + 3 = 5, subtract 3 from both sides.`, videoRef: null,
  })
  await lesson('intro', 'What is algebra?', true)
  const paid = await lesson('solving', 'Solving equations', false)
  const quiz = await call<{ id: string }>(r, 'POST', `/api/admin/courses/${course.id}/quizzes`, {
    title: 'Check yourself', description: null, lessonId: null, passScorePct: 50, timeLimitSeconds: null, maxAttempts: null, isPreview: false, status: 'published',
  })
  await call(r, 'POST', `/api/admin/quizzes/${quiz.id}/questions`, {
    prompt: 'Solve x + 3 = 5', explanation: 'Subtract 3.', marks: 1,
    options: [{ id: null, label: 'x = 2', isCorrect: true }, { id: null, label: 'x = 8', isCorrect: false }],
  })
  const editor = await call<Record<string, unknown>>(r, 'GET', `/api/admin/courses/${course.id}`)
  await call(r, 'PUT', `/api/admin/courses/${course.id}`, { ...editor, status: 'published' })
  await call(r, 'PUT', `/api/admin/courses/${course.id}/price`, {
    priceMinor, promoPriceMinor: null, promoStartsAt: null, promoEndsAt: null, accessDays: null, status: 'active',
  })
  return { id: course.id, slug, title, paidLessonId: paid.id, quizId: quiz.id }
}

/** Seeds as the owner in a separate context, so the test's own page stays signed out until it signs in. */
export async function asOwner<T>(page: Page, work: (owner: Page) => Promise<T>) {
  const ctx = await page.context().browser()!.newContext({ baseURL })
  const owner = await ctx.newPage()
  await signIn(owner, 'owner@msr.test', ['owner_admin'])
  try { return await work(owner) } finally { await ctx.close() }
}

/** Fails the test on any page error or Content-Security-Policy violation. */
export function watchErrors(page: Page) {
  const problems: string[] = []
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`))
  page.on('console', (m) => {
    if (m.type() === 'error' && /Content Security Policy|Refused to/i.test(m.text())) problems.push(`csp: ${m.text()}`)
  })
  return () => expect(problems).toEqual([])
}

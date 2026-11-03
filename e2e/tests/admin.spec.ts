import { expect, test } from '@playwright/test'
import { asOwner, seedCourse, signIn, unique, watchErrors } from './helpers'

test('finance refunds a sale and the learner loses access', async ({ page, browser }) => {
  const course = await asOwner(page, (owner) => seedCourse(owner, 90_000))
  const email = `${unique('learner')}@msr.test`
  await signIn(page, email)
  await page.goto(`/courses/${course.slug}`)
  await page.getByRole('button', { name: 'Buy this course' }).click()
  await page.getByRole('button', { name: /^Pay/ }).click()
  await expect(page.getByRole('heading', { name: 'Payment confirmed' })).toBeVisible()

  const ctx = await browser.newContext()
  const finance = await ctx.newPage()
  const done = watchErrors(finance)
  await finance.goto('/')
  await signIn(finance, `${unique('finance')}@msr.test`, ['finance_manager'])
  await finance.goto(`/admin/orders?q=${encodeURIComponent(email)}`)
  await finance.locator('tbody a').first().click()
  await finance.getByLabel('Reason').fill('Requested by learner')
  finance.once('dialog', (d) => d.accept())
  await finance.getByRole('button', { name: 'Refund', exact: true }).click()
  await expect(finance.getByText('Requested by learner')).toBeVisible()
  await expect(finance.getByText('Revoked')).toBeVisible()
  done()
  await ctx.close()

  await page.goto(`/courses/${course.slug}/lessons/${course.paidLessonId}`)
  await expect(page.getByText('This content is locked')).toBeVisible()
})

test('every admin screen loads without errors or CSP violations', async ({ page }) => {
  const done = watchErrors(page)
  await page.goto('/')
  await signIn(page, 'owner@msr.test', ['owner_admin'])
  for (const path of ['/admin', '/admin/users', '/admin/courses', '/admin/quizzes', '/admin/drive', '/admin/pricing', '/admin/plans',
    '/admin/orders', '/admin/reconciliation', '/admin/entitlements', '/admin/reports', '/admin/audit', '/admin/settings']) {
    await page.goto(path)
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
    await expect(page.getByText('Could not load')).toHaveCount(0)
  }
  done()
})

test('learners are kept out of the admin console', async ({ page }) => {
  await page.goto('/')
  await signIn(page, `${unique('learner')}@msr.test`)
  await page.goto('/admin/orders')
  await expect(page).toHaveURL(/forbidden|\/app/)
  expect((await page.request.get('/api/admin/orders')).status()).toBe(403)
})

import { expect, test } from '@playwright/test'
import { asOwner, seedCourse, signIn, unique, watchErrors } from './helpers'

test('a learner buys a course, studies it and keeps the receipt @mobile', async ({ page }) => {
  const done = watchErrors(page)
  const course = await asOwner(page, (owner) => seedCourse(owner))
  await signIn(page, `${unique('learner')}@msr.test`)

  await page.goto(`/courses/${course.slug}`)
  await expect(page.getByRole('heading', { name: course.title })).toBeVisible()
  await page.goto(`/courses/${course.slug}/lessons/${course.paidLessonId}`)
  await expect(page.getByText('This content is locked')).toBeVisible()

  await page.goto(`/courses/${course.slug}`)
  await page.getByRole('button', { name: 'Buy this course' }).click()
  await expect(page.getByRole('heading', { name: 'Sandbox payment' })).toBeVisible()
  await expect(page.getByText('BDT 1,500').first()).toBeVisible()
  await page.getByRole('button', { name: /^Pay/ }).click()
  await expect(page.getByRole('heading', { name: 'Payment confirmed' })).toBeVisible()

  await page.getByRole('link', { name: 'Start learning' }).click()
  await expect(page.getByText('Unlocked')).toBeVisible()
  await page.goto(`/courses/${course.slug}/lessons/${course.paidLessonId}`)
  await expect(page.getByText('subtract 3 from both sides').first()).toBeVisible()

  await page.goto(`/courses/${course.slug}/quizzes/${course.quizId}`)
  await page.getByRole('button', { name: 'Start quiz' }).click()
  await page.getByText('x = 2').click()
  await page.getByRole('button', { name: /submit/i }).click()
  await expect(page.getByText('Passed').first()).toBeVisible()

  await page.goto('/app/receipts')
  await page.getByRole('link', { name: /^R-/ }).first().click()
  await expect(page.getByText('Billed to')).toBeVisible()
  await expect(page.getByText(course.title)).toBeVisible()
  done()
})

test('a declined payment unlocks nothing and can be retried @mobile', async ({ page }) => {
  const done = watchErrors(page)
  const course = await asOwner(page, (owner) => seedCourse(owner))
  await signIn(page, `${unique('learner')}@msr.test`)

  await page.goto(`/courses/${course.slug}`)
  await page.getByRole('button', { name: 'Buy this course' }).click()
  await page.getByRole('button', { name: 'Decline' }).click()
  await expect(page.getByRole('heading', { name: 'Payment not completed' })).toBeVisible()
  await page.goto(`/courses/${course.slug}/lessons/${course.paidLessonId}`)
  await expect(page.getByText('This content is locked')).toBeVisible()

  await page.goto(`/courses/${course.slug}`)
  await page.getByRole('button', { name: 'Buy this course' }).click()
  await page.getByRole('button', { name: /^Pay/ }).click()
  await expect(page.getByRole('heading', { name: 'Payment confirmed' })).toBeVisible()
  await page.goto('/app/transactions')
  await expect(page.getByText('Failed')).toBeVisible()
  await expect(page.getByText('Successful')).toBeVisible()
  done()
})

test('signed-out visitors are sent to sign in before checkout', async ({ page }) => {
  const course = await asOwner(page, (owner) => seedCourse(owner))
  await page.goto(`/courses/${course.slug}`)
  await page.getByRole('button', { name: 'Buy this course' }).click()
  await expect(page).toHaveURL(/\/login\?next=/)
})

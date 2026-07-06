import { test, expect } from '@playwright/test'

test('landing page is public and shows the page view badge', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { name: 'Get started' })).toBeVisible()
  await expect(page.getByRole('link', { name: /sign in/i })).toBeVisible()
  // the badge proves the public page-visit endpoints round-trip the real DB
  await expect(page.getByText(/\d+ views/)).toBeVisible()
})

test('unauthenticated visitor is redirected from the dashboard', async ({ page }) => {
  await page.goto('/dashboard')

  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: 'Get started' })).toBeVisible()
})

test('full login journey: sign in, dashboard, reload, logout', async ({ page }) => {
  await page.goto('/')

  // full redirect chain: API login -> test provider bounce -> API callback ->
  // SPA /auth/callback (handoff code in fragment) -> token exchange -> dashboard
  await page.getByRole('link', { name: /sign in/i }).click()
  await expect(page).toHaveURL(/\/dashboard$/)
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()

  // profile comes from GET /auth/me with the minted bearer token
  await expect(page.getByText('E2E Test User')).toBeVisible()
  await expect(page.getByText('e2e@example.com')).toBeVisible()

  // protected per-page stats table and the badge both render
  await expect(page.getByRole('table')).toBeVisible()
  await expect(page.getByText(/\d+ views/)).toBeVisible()

  // the session survives a reload via the stored refresh token
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()

  // logout revokes the session and returns to the landing page
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByRole('heading', { name: 'Get started' })).toBeVisible()

  // and the dashboard is locked again
  await page.goto('/dashboard')
  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: 'Get started' })).toBeVisible()
})

test('authenticated visitor sees the dashboard link on the landing page', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('link', { name: /sign in/i }).click()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()

  await page.goto('/')

  await expect(page.getByRole('link', { name: 'Go to dashboard' })).toBeVisible()
  await expect(page.getByRole('link', { name: /sign in/i })).not.toBeVisible()
})

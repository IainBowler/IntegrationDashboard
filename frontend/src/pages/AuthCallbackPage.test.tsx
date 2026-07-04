import { afterEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { http, HttpResponse } from 'msw'
import { server } from '../mocks/server'
import { mockTokenResponse } from '../mocks/handlers'
import { AppRoutes } from '../AppRoutes'
import { AuthProvider } from '../auth/AuthContext'

const API_BASE = 'http://localhost:3000'

function renderCallbackPage() {
  return render(
    <MemoryRouter initialEntries={['/auth/callback']}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('AuthCallbackPage', () => {
  afterEach(() => {
    window.history.replaceState(null, '', '/')
  })

  it('exchanges the handoff code, scrubs the URL, and lands on the dashboard', async () => {
    window.history.replaceState(null, '', '/auth/callback#code=handoff-1')
    let exchangedCode: string | null = null
    server.use(
      http.post(`${API_BASE}/auth/token`, async ({ request }) => {
        const body = (await request.json()) as { code: string }
        exchangedCode = body.code
        return HttpResponse.json(mockTokenResponse)
      }),
    )

    renderCallbackPage()

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(exchangedCode).toBe('handoff-1')
    expect(window.location.hash).toBe('')
  })

  it('shows an error with a way home when the IdP reported a failure', async () => {
    window.history.replaceState(null, '', '/auth/callback#error=login_failed')

    renderCallbackPage()

    expect(await screen.findByRole('heading', { name: 'Sign-in failed' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Return to the home page' })).toBeInTheDocument()
  })

  it('shows an error when the handoff code is rejected', async () => {
    window.history.replaceState(null, '', '/auth/callback#code=already-used')
    server.use(
      http.post(`${API_BASE}/auth/token`, () => new HttpResponse(null, { status: 401 })),
    )

    renderCallbackPage()

    expect(await screen.findByRole('heading', { name: 'Sign-in failed' })).toBeInTheDocument()
  })
})

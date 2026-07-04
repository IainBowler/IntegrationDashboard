import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { AppRoutes } from './AppRoutes'
import { AuthProvider } from './auth/AuthContext'

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('AppRoutes', () => {
  it('shows the landing page with a sign-in link and the page view badge', async () => {
    renderAt('/')

    expect(screen.getByText('Get started')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sign in with Okta' })).toBeInTheDocument()
    // badge uses the router's pathname; MSW returns 0 for '/'
    expect(await screen.findByText('0 views')).toBeInTheDocument()
  })

  it('shows the page view badge on the dashboard too', async () => {
    sessionStorage.setItem('refreshToken', 'refresh-token-1')

    renderAt('/dashboard')

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    // MSW returns 42 for '/dashboard'
    expect(await screen.findByText('42 views')).toBeInTheDocument()
  })

  it('offers the dashboard link instead of sign-in when already authenticated', async () => {
    sessionStorage.setItem('refreshToken', 'refresh-token-1')

    renderAt('/')

    expect(await screen.findByRole('link', { name: 'Go to dashboard' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Sign in with Okta' })).not.toBeInTheDocument()
  })
})

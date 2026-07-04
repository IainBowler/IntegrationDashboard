import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '../mocks/server'
import { AuthProvider } from './AuthContext'
import { useAuth } from './useAuth'

const API_BASE = 'http://localhost:3000'

function AuthProbe() {
  const { user, isAuthenticated, isInitializing } = useAuth()
  if (isInitializing) return <p>initializing</p>
  return <p>{isAuthenticated ? `signed in as ${user!.email}` : 'signed out'}</p>
}

function renderProbe() {
  return render(
    <AuthProvider>
      <AuthProbe />
    </AuthProvider>,
  )
}

describe('AuthProvider', () => {
  it('is signed out immediately when no session is stored', () => {
    renderProbe()

    expect(screen.getByText('signed out')).toBeInTheDocument()
  })

  it('restores the session from a stored refresh token', async () => {
    sessionStorage.setItem('refreshToken', 'refresh-token-1')

    renderProbe()

    expect(await screen.findByText('signed in as user@example.com')).toBeInTheDocument()
  })

  it('ends up signed out and clears storage when the stored token is rejected', async () => {
    sessionStorage.setItem('refreshToken', 'expired-refresh')
    server.use(
      http.post(`${API_BASE}/auth/refresh`, () => new HttpResponse(null, { status: 401 })),
    )

    renderProbe()

    expect(await screen.findByText('signed out')).toBeInTheDocument()
    expect(sessionStorage.getItem('refreshToken')).toBeNull()
  })
})

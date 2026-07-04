import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AuthProvider } from './AuthContext'
import { ProtectedRoute } from './ProtectedRoute'

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <Routes>
          <Route path="/" element={<p>landing page</p>} />
          <Route element={<ProtectedRoute />}>
            <Route path="/dashboard" element={<p>secret dashboard</p>} />
          </Route>
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('ProtectedRoute', () => {
  it('redirects unauthenticated visitors to the landing page', () => {
    renderAt('/dashboard')

    expect(screen.getByText('landing page')).toBeInTheDocument()
    expect(screen.queryByText('secret dashboard')).not.toBeInTheDocument()
  })

  it('renders the protected content once the stored session is restored', async () => {
    sessionStorage.setItem('refreshToken', 'refresh-token-1')

    renderAt('/dashboard')

    expect(await screen.findByText('secret dashboard')).toBeInTheDocument()
  })
})

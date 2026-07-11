import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { AppRoutes } from '../AppRoutes'
import { AuthProvider } from '../auth/AuthContext'

function renderDashboard() {
  sessionStorage.setItem('refreshToken', 'refresh-token-1')
  return render(
    <MemoryRouter initialEntries={['/dashboard']}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('DashboardPage', () => {
  it('shows the signed-in user profile', async () => {
    renderDashboard()

    expect(await screen.findByText('Test User')).toBeInTheDocument()
    expect(screen.getByText('user@example.com')).toBeInTheDocument()
    expect(screen.getByText('okta')).toBeInTheDocument()
  })

  it('shows per-page visit counts', async () => {
    renderDashboard()

    expect(await screen.findByRole('table')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('lists the integrations with a details button that opens the integration page', async () => {
    renderDashboard()

    const integrationsSection = await screen.findByRole('region', { name: 'Integrations' })
    expect(integrationsSection).toHaveTextContent('Salesforce')

    await userEvent.click(screen.getByRole('button', { name: 'Details' }))

    expect(
      await screen.findByRole('heading', { name: 'Salesforce integration' }),
    ).toBeInTheDocument()
  })

  it('signs the user out and returns to the landing page', async () => {
    renderDashboard()
    const signOutButton = await screen.findByRole('button', { name: 'Sign out' })

    await userEvent.click(signOutButton)

    expect(await screen.findByText('Get started')).toBeInTheDocument()
    expect(sessionStorage.getItem('refreshToken')).toBeNull()
  })
})

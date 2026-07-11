import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { http, HttpResponse } from 'msw'
import { AppRoutes } from '../AppRoutes'
import { AuthProvider } from '../auth/AuthContext'
import { server } from '../mocks/server'

function renderAt(path: string) {
  sessionStorage.setItem('refreshToken', 'refresh-token-1')
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('IntegrationDetailPage', () => {
  it('shows the integration name and no statuses before any call', async () => {
    renderAt('/integrations/salesforce')

    expect(
      await screen.findByRole('heading', { name: 'Salesforce integration' }),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Auth endpoint status')).toHaveTextContent('—')
    expect(screen.getByLabelText('Accounts endpoint status')).toHaveTextContent('—')
  })

  it('calls the auth endpoint and shows its status code', async () => {
    renderAt('/integrations/salesforce')
    const button = await screen.findByRole('button', { name: 'Call auth endpoint' })

    await userEvent.click(button)

    expect(await screen.findByText('HTTP 200')).toBeInTheDocument()
  })

  it('calls the accounts endpoint and shows its status code', async () => {
    renderAt('/integrations/salesforce')
    const button = await screen.findByRole('button', { name: 'Call accounts endpoint' })

    await userEvent.click(button)

    expect(await screen.findByText('HTTP 200')).toBeInTheDocument()
  })

  it('shows the failure status code when an endpoint fails', async () => {
    server.use(
      http.get('http://localhost:3000/api/integrations/salesforce/auth', () =>
        HttpResponse.json({ title: 'Salesforce request failed' }, { status: 502 }),
      ),
    )
    renderAt('/integrations/salesforce')
    const button = await screen.findByRole('button', { name: 'Call auth endpoint' })

    await userEvent.click(button)

    expect(await screen.findByText('HTTP 502')).toBeInTheDocument()
  })

  it('links back to the dashboard', async () => {
    renderAt('/integrations/salesforce')

    const back = await screen.findByRole('link', { name: 'Back to dashboard' })
    await userEvent.click(back)

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })
})

import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
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
    expect(screen.getByLabelText('Leads endpoint status')).toHaveTextContent('—')
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

  it('creates a sample lead and shows the created status code', async () => {
    renderAt('/integrations/salesforce')
    const button = await screen.findByRole('button', { name: 'Create sample lead' })

    await userEvent.click(button)

    expect(await screen.findByText('HTTP 201')).toBeInTheDocument()
  })

  it('shows the failure status code when the lead create fails', async () => {
    server.use(
      http.post('http://localhost:3000/api/integrations/salesforce/leads', () =>
        HttpResponse.json({ title: 'Salesforce request failed' }, { status: 502 }),
      ),
    )
    renderAt('/integrations/salesforce')
    const button = await screen.findByRole('button', { name: 'Create sample lead' })

    await userEvent.click(button)

    expect(await screen.findByText('HTTP 502')).toBeInTheDocument()
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

  it('shows lifetime statistics per endpoint, with dashes for endpoints never called', async () => {
    renderAt('/integrations/salesforce')

    const statistics = await screen.findByRole('region', { name: 'Statistics' })
    const rows = await within(statistics).findAllByRole('row')
    // header + the six endpoints from the mock
    expect(rows).toHaveLength(7)

    const authRow = within(statistics).getByRole('row', { name: /^auth / })
    expect(authRow).toHaveTextContent('Inbound')
    expect(authRow).toHaveTextContent('5')
    expect(authRow).toHaveTextContent('80%')
    expect(authRow).toHaveTextContent('13') // 12.5 rounded
    expect(authRow).toHaveTextContent('HTTP 200')

    const tokenRow = within(statistics).getByRole('row', { name: /^token / })
    expect(tokenRow).toHaveTextContent('Outbound')
    expect(tokenRow).toHaveTextContent('—')
  })

  it('refreshes the statistics after an endpoint check completes', async () => {
    renderAt('/integrations/salesforce')
    const statistics = await screen.findByRole('region', { name: 'Statistics' })
    await within(statistics).findAllByRole('row')

    server.use(
      http.get('http://localhost:3000/api/integrations/salesforce/statistics', () =>
        HttpResponse.json({
          name: 'salesforce',
          displayName: 'Salesforce',
          endpoints: [
            {
              endpointName: 'auth',
              direction: 'Inbound',
              totalCalls: 99,
              successCount: 99,
              avgDurationMs: 10,
              maxDurationMs: 20,
              lastCalledAtUtc: '2026-07-12T10:00:00Z',
              lastStatusCode: 200,
            },
          ],
        }),
      ),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Call auth endpoint' }))

    expect(await within(statistics).findByText('99')).toBeInTheDocument()
  })

  it('still renders the endpoint check buttons when statistics fail to load', async () => {
    server.use(
      http.get('http://localhost:3000/api/integrations/salesforce/statistics', () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    )
    renderAt('/integrations/salesforce')

    expect(
      await screen.findByRole('button', { name: 'Call auth endpoint' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Loading statistics…')).toBeInTheDocument()
  })

  it('links back to the dashboard', async () => {
    renderAt('/integrations/salesforce')

    const back = await screen.findByRole('link', { name: 'Back to dashboard' })
    await userEvent.click(back)

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })
})

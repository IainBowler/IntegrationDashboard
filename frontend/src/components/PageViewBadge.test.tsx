import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { PageViewBadge } from './PageViewBadge'

const API_BASE = 'http://localhost:3000'

describe('PageViewBadge', () => {
  it('records a page visit on mount', async () => {
    const recordedPaths: string[] = []
    server.use(
      http.post(`${API_BASE}/page-visits`, async ({ request }) => {
        const body = (await request.json()) as { pagePath: string }
        recordedPaths.push(body.pagePath)
        return new HttpResponse(null, { status: 201 })
      })
    )

    render(<PageViewBadge pagePath="/dashboard" />)

    await waitFor(() => expect(recordedPaths).toContain('/dashboard'))
  })

  it('displays the visit count returned by the API', async () => {
    render(<PageViewBadge pagePath="/dashboard" />)

    await screen.findByText('42 views')
  })

  it('formats large counts with locale separators', async () => {
    server.use(
      http.get(`${API_BASE}/page-visits/count`, () =>
        HttpResponse.json({ count: 1234567 })
      )
    )

    render(<PageViewBadge pagePath="/any" />)

    await screen.findByText('1,234,567 views')
  })

  it('renders nothing while waiting for the API', () => {
    server.use(
      http.get(`${API_BASE}/page-visits/count`, () =>
        new Promise(() => {})
      )
    )

    const { container } = render(<PageViewBadge pagePath="/slow" />)

    expect(container.firstChild).toBeNull()
  })

  it('has an accessible label with the count', async () => {
    render(<PageViewBadge pagePath="/dashboard" />)

    const badge = await screen.findByRole('generic', { name: /42 page views/i })
    expect(badge).toBeInTheDocument()
  })
})

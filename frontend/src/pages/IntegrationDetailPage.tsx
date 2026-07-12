import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import {
  checkIntegrationAuth,
  fetchIntegrationAccounts,
  getIntegrationStatistics,
} from '../api/integrations'
import type { EndpointStatistics, IntegrationStatistics } from '../api/integrations'
import '../App.css'

interface EndpointCheckProps {
  label: string
  buttonText: string
  call: () => Promise<number>
  onComplete: () => void
}

function EndpointCheck({ label, buttonText, call, onComplete }: EndpointCheckProps) {
  const [status, setStatus] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleClick() {
    setBusy(true)
    try {
      setStatus(await call())
    } catch {
      // network-level failure — no HTTP status to show
      setStatus(null)
    } finally {
      setBusy(false)
      onComplete()
    }
  }

  return (
    <p>
      <button type="button" className="counter" onClick={handleClick} disabled={busy}>
        {buttonText}
      </button>{' '}
      <span aria-label={`${label} status`}>{status === null ? '—' : `HTTP ${status}`}</span>
    </p>
  )
}

function successRate(endpoint: EndpointStatistics): string {
  return endpoint.totalCalls > 0
    ? `${Math.round((100 * endpoint.successCount) / endpoint.totalCalls)}%`
    : '—'
}

function lastCall(endpoint: EndpointStatistics): string {
  if (!endpoint.lastCalledAtUtc) return '—'
  const when = new Date(endpoint.lastCalledAtUtc).toLocaleString()
  return endpoint.lastStatusCode === null ? when : `${when} (HTTP ${endpoint.lastStatusCode})`
}

export function IntegrationDetailPage() {
  const { name = '' } = useParams()
  const [stats, setStats] = useState<IntegrationStatistics | null>(null)

  const loadStats = useCallback(() => {
    getIntegrationStatistics(name).then(setStats).catch(console.error)
  }, [name])

  useEffect(() => {
    loadStats()
  }, [loadStats])

  return (
    <main className="dashboard">
      <header className="dashboard-header">
        <h1>{stats?.displayName ?? name} integration</h1>
        <Link to="/dashboard">Back to dashboard</Link>
      </header>

      <section aria-label="Endpoint checks">
        <h2>Endpoint checks</h2>
        <EndpointCheck
          label="Auth endpoint"
          buttonText="Call auth endpoint"
          call={() => checkIntegrationAuth(name)}
          onComplete={loadStats}
        />
        <EndpointCheck
          label="Accounts endpoint"
          buttonText="Call accounts endpoint"
          call={() => fetchIntegrationAccounts(name)}
          onComplete={loadStats}
        />
      </section>

      <section aria-label="Statistics">
        <h2>Statistics</h2>
        {stats ? (
          <table>
            <thead>
              <tr>
                <th scope="col">Endpoint</th>
                <th scope="col">Direction</th>
                <th scope="col">Calls</th>
                <th scope="col">Success rate</th>
                <th scope="col">Avg ms</th>
                <th scope="col">Max ms</th>
                <th scope="col">Last call</th>
              </tr>
            </thead>
            <tbody>
              {stats.endpoints.map((endpoint) => (
                <tr key={endpoint.endpointName}>
                  <td>{endpoint.endpointName}</td>
                  <td>{endpoint.direction}</td>
                  <td>{endpoint.totalCalls.toLocaleString()}</td>
                  <td>{successRate(endpoint)}</td>
                  <td>{endpoint.avgDurationMs === null ? '—' : Math.round(endpoint.avgDurationMs)}</td>
                  <td>{endpoint.maxDurationMs === null ? '—' : endpoint.maxDurationMs}</td>
                  <td>{lastCall(endpoint)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <p>Loading statistics…</p>
        )}
      </section>
    </main>
  )
}

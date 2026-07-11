import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { checkIntegrationAuth, fetchIntegrationAccounts, integrations } from '../api/integrations'
import '../App.css'

interface EndpointCheckProps {
  label: string
  buttonText: string
  call: () => Promise<number>
}

function EndpointCheck({ label, buttonText, call }: EndpointCheckProps) {
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

export function IntegrationDetailPage() {
  const { name = '' } = useParams()
  const label = integrations.find((i) => i.name === name)?.label ?? name

  return (
    <main className="dashboard">
      <header className="dashboard-header">
        <h1>{label} integration</h1>
        <Link to="/dashboard">Back to dashboard</Link>
      </header>

      <section aria-label="Endpoint checks">
        <h2>Endpoint checks</h2>
        <EndpointCheck
          label="Auth endpoint"
          buttonText="Call auth endpoint"
          call={() => checkIntegrationAuth(name)}
        />
        <EndpointCheck
          label="Accounts endpoint"
          buttonText="Call accounts endpoint"
          call={() => fetchIntegrationAccounts(name)}
        />
      </section>
    </main>
  )
}

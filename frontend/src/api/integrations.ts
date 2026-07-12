import { authFetch } from './http'

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? ''

export interface Integration {
  /** route key, matches the API's /api/integrations/{name} segment */
  name: string
  displayName: string
}

export interface EndpointStatistics {
  endpointName: string
  direction: 'Inbound' | 'Outbound'
  totalCalls: number
  successCount: number
  avgDurationMs: number | null
  maxDurationMs: number | null
  lastCalledAtUtc: string | null
  lastStatusCode: number | null
}

export interface IntegrationStatistics {
  name: string
  displayName: string
  endpoints: EndpointStatistics[]
}

export async function getIntegrations(): Promise<Integration[]> {
  const res = await authFetch(`${API_BASE}/api/integrations`)
  if (!res.ok) throw new Error(`Failed to load integrations (HTTP ${res.status})`)
  return (await res.json()) as Integration[]
}

export async function getIntegrationStatistics(name: string): Promise<IntegrationStatistics> {
  const res = await authFetch(`${API_BASE}/api/integrations/${name}/statistics`)
  if (!res.ok) throw new Error(`Failed to load statistics (HTTP ${res.status})`)
  return (await res.json()) as IntegrationStatistics
}

/** Calls the integration's auth-check endpoint and returns the HTTP status code. */
export async function checkIntegrationAuth(name: string): Promise<number> {
  const res = await authFetch(`${API_BASE}/api/integrations/${name}/auth`)
  return res.status
}

/** Calls the integration's accounts endpoint and returns the HTTP status code. */
export async function fetchIntegrationAccounts(name: string): Promise<number> {
  const res = await authFetch(`${API_BASE}/api/integrations/${name}/accounts`)
  return res.status
}

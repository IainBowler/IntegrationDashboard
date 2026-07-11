import { authFetch } from './http'

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? ''

export interface Integration {
  /** route key, matches the API's /api/integrations/{name} segment */
  name: string
  label: string
}

// Hardcoded for now — the API doesn't expose an integration list endpoint yet.
export const integrations: Integration[] = [{ name: 'salesforce', label: 'Salesforce' }]

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

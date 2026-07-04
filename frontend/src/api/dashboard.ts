import { authFetch } from './http'
import type { AuthUser } from './authClient'

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? ''

export interface PageVisitSummaryItem {
  pagePath: string
  count: number
}

export async function getMe(): Promise<AuthUser | null> {
  const res = await authFetch(`${API_BASE}/auth/me`)
  return res.ok ? ((await res.json()) as AuthUser) : null
}

export async function getPageVisitSummary(): Promise<PageVisitSummaryItem[] | null> {
  const res = await authFetch(`${API_BASE}/page-visits/summary`)
  if (!res.ok) return null
  const data = (await res.json()) as { pages: PageVisitSummaryItem[] }
  return data.pages
}

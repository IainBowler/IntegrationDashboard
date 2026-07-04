import { refreshSession } from './authClient'
import type { TokenResponse } from './authClient'
import { clearSession, getAccessToken, getRefreshToken, storeSession } from '../auth/session'

let refreshInFlight: Promise<TokenResponse | null> | null = null

/**
 * Refreshes the session using the stored refresh token. Single-flight: any
 * number of concurrent callers (bootstrap, parallel 401 retries) share one
 * refresh call, which matters because refresh tokens rotate on use.
 */
export function refreshCurrentSession(): Promise<TokenResponse | null> {
  refreshInFlight ??= doRefresh().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

async function doRefresh(): Promise<TokenResponse | null> {
  const refreshToken = getRefreshToken()
  if (!refreshToken) return null
  const tokens = await refreshSession(refreshToken).catch(() => null)
  if (!tokens) {
    clearSession()
    return null
  }
  storeSession(tokens)
  return tokens
}

/**
 * fetch with the Authorization header attached; on a 401 it refreshes the
 * session once and retries. A failed refresh clears the session (which the
 * auth context observes) and returns the original 401.
 */
export async function authFetch(input: string, init: RequestInit = {}): Promise<Response> {
  const response = await fetchWithToken(input, init)
  if (response.status !== 401) return response

  const tokens = await refreshCurrentSession()
  if (!tokens) return response

  return fetchWithToken(input, init)
}

function fetchWithToken(input: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers)
  const token = getAccessToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  return fetch(input, { ...init, headers })
}

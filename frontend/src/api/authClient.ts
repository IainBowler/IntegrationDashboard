const API_BASE = import.meta.env.VITE_API_BASE_URL ?? ''

export interface AuthUser {
  userId: number
  provider: string
  email: string | null
  displayName: string | null
}

export interface TokenResponse {
  accessToken: string
  expiresInSeconds: number
  refreshToken: string
  user: AuthUser
}

export function getLoginUrl(provider: string = 'okta'): string {
  return `${API_BASE}/auth/login/${provider}`
}

export async function exchangeCode(code: string): Promise<TokenResponse | null> {
  const res = await fetch(`${API_BASE}/auth/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
  })
  return res.ok ? ((await res.json()) as TokenResponse) : null
}

export async function refreshSession(refreshToken: string): Promise<TokenResponse | null> {
  const res = await fetch(`${API_BASE}/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  })
  return res.ok ? ((await res.json()) as TokenResponse) : null
}

export async function logout(refreshToken: string): Promise<void> {
  await fetch(`${API_BASE}/auth/logout`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  })
}

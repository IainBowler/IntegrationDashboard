// Access token lives in memory only; the refresh token sits in sessionStorage
// so a page reload can silently restore the session but closing the tab ends it.
const REFRESH_TOKEN_KEY = 'refreshToken'

let accessToken: string | null = null
const sessionClearedListeners = new Set<() => void>()

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  accessToken = token
}

export function getRefreshToken(): string | null {
  return sessionStorage.getItem(REFRESH_TOKEN_KEY)
}

export function storeSession(tokens: { accessToken: string; refreshToken: string }): void {
  accessToken = tokens.accessToken
  sessionStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken)
}

export function clearSession(): void {
  accessToken = null
  sessionStorage.removeItem(REFRESH_TOKEN_KEY)
  for (const listener of sessionClearedListeners) listener()
}

export function onSessionCleared(listener: () => void): () => void {
  sessionClearedListeners.add(listener)
  return () => sessionClearedListeners.delete(listener)
}

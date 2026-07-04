import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { logout as apiLogout } from '../api/authClient'
import type { AuthUser, TokenResponse } from '../api/authClient'
import { refreshCurrentSession } from '../api/http'
import { AuthContext } from './context'
import type { AuthContextValue } from './context'
import { clearSession, getRefreshToken, onSessionCleared, storeSession } from './session'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null)
  // only initializing if there is a stored session to restore
  const [isInitializing, setIsInitializing] = useState(() => getRefreshToken() !== null)

  useEffect(() => {
    if (getRefreshToken() === null) return
    let cancelled = false
    void refreshCurrentSession().then((tokens) => {
      if (cancelled) return
      setUser(tokens?.user ?? null)
      setIsInitializing(false)
    })
    return () => {
      cancelled = true
    }
  }, [])

  // authFetch clears the session when a refresh fails mid-use; reflect that here
  useEffect(() => onSessionCleared(() => setUser(null)), [])

  const setSession = useCallback((tokens: TokenResponse) => {
    storeSession(tokens)
    setUser(tokens.user)
  }, [])

  const logout = useCallback(() => {
    const refreshToken = getRefreshToken()
    if (refreshToken) {
      void apiLogout(refreshToken).catch(() => {})
    }
    clearSession()
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAuthenticated: user !== null,
      isInitializing,
      setSession,
      logout,
    }),
    [user, isInitializing, setSession, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

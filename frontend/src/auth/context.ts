import { createContext } from 'react'
import type { AuthUser, TokenResponse } from '../api/authClient'

export interface AuthContextValue {
  user: AuthUser | null
  isAuthenticated: boolean
  isInitializing: boolean
  setSession: (tokens: TokenResponse) => void
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

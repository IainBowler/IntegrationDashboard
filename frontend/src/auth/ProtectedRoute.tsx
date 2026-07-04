import { Navigate, Outlet } from 'react-router'
import { useAuth } from './useAuth'

export function ProtectedRoute() {
  const { isAuthenticated, isInitializing } = useAuth()

  if (isInitializing) return null
  if (!isAuthenticated) return <Navigate to="/" replace />
  return <Outlet />
}

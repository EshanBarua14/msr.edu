import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { FullPageSpinner } from '@/components/ui/Spinner'
import { useAuth } from './AuthProvider'
import { hasAnyRole, type Role } from './roles'

/**
 * UI-level route guards. These only shape navigation; real enforcement is
 * Row Level Security and server-side checks in Supabase.
 */
export function RequireAuth() {
  const { status } = useAuth()
  const location = useLocation()
  if (status === 'loading') return <FullPageSpinner />
  if (status === 'signed_out') {
    const next = location.pathname + location.search
    return <Navigate to={`/login?next=${encodeURIComponent(next)}`} replace />
  }
  return <Outlet />
}

export function RequireRole({ anyOf }: { anyOf: readonly Role[] }) {
  const { roles } = useAuth()
  if (!hasAnyRole(roles, anyOf)) return <Navigate to="/forbidden" replace />
  return <Outlet />
}

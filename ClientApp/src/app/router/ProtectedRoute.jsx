import { Navigate, Outlet, useLocation } from 'react-router-dom'
import LoadingState from '@/shared/components/LoadingState'
import { hasRole } from '@/shared/auth/permissions'
import { useAuthStore } from '@/modules/auth/stores/authStore'
import { useCurrentUser } from '@/modules/auth/hooks/useAuth'

export default function ProtectedRoute({ allowedRoles }) {
  const location = useLocation()
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  const roles = useAuthStore((state) => state.roles)
  const { isLoading } = useCurrentUser()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (isLoading) {
    return (
      <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
        <LoadingState message="Đang tải..." />
      </div>
    )
  }

  if (allowedRoles?.length && !hasRole(roles, allowedRoles)) {
    return <Navigate to="/forbidden" replace state={{ from: location.pathname }} />
  }

  return <Outlet />
}

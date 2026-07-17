import { Navigate, Outlet } from 'react-router-dom'
import { useAuthStore } from '@/modules/auth/stores/authStore'

export default function GuestRoute() {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)

  if (isAuthenticated) {
    return <Navigate to="/dashboard" replace />
  }

  return <Outlet />
}
